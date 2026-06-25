using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using CHMGenerator.WPF.Models;
using CHMGenerator.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CHMGenerator.WPF.ViewModels;

[SupportedOSPlatform("windows")]
public partial class MainViewModel : ObservableObject
{
    private readonly WordToHtmlConverter _wordConverter = new();
    private readonly ChmProjectGenerator _projectGenerator = new();
    private readonly ChmCompiler _compiler = new();

    [ObservableProperty] private string _projectTitle = "帮助文档";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _hhcStatus = "";
    [ObservableProperty] private bool _fullTextSearch = true;
    [ObservableProperty] private bool _binaryToc = true;
    [ObservableProperty] private bool _autoIndex = true;
    [ObservableProperty] private string _lastChmPath = "";
    [ObservableProperty] private string _logText = "";

    // 监听 StatusText 变化，自动记录到日志
    partial void OnStatusTextChanged(string value)
    {
        LogManager.Instance.WriteStatus(value);
    }

    /// <summary>文档树根节点列表</summary>
    public ObservableCollection<DocumentNode> RootNodes { get; } = new();

    private DocumentNode? _currentListeningNode;

    /// <summary>当前选中的节点</summary>
    [ObservableProperty] private DocumentNode? _selectedNode;

    /// <summary>右侧实时预览的扁平化条目</summary>
    public ObservableCollection<PreviewItem> PreviewItems { get; } = new();

    public MainViewModel()
    {
        // 初始化日志管理
        LogManager.Instance.StartNewSession();
        LogManager.Instance.WriteOperation("程序启动");
        LogManager.Instance.CleanOldLogs(30); // 清理30天前的日志

        UpdateHhcStatus();
        RootNodes.CollectionChanged += (_, _) => RefreshPreview();
        Helpers.TreeViewDragDropHelper.NodeDropped += OnNodeDropped;
        Helpers.TreeViewDragDropHelper.NodeDroppedToRoot += OnNodeDroppedToRoot;
        Helpers.TreeViewDragDropHelper.BlankAreaClicked += OnBlankAreaClicked;
        Helpers.TreeViewDragDropHelper.ExternalFilesDropped += OnExternalFilesDropped;
        // v4: 订阅拖拽过程事件，在状态栏显示当前落点提示
        Helpers.TreeViewDragDropHelper.DropPositionChanged += OnDropPositionChanged;
        Helpers.TreeViewDragDropHelper.DragEnded += OnDragEnded;
    }

    /// <summary>
    /// 拖拽过程中显示当前落点提示（覆盖原 StatusText）
    /// </summary>
    private void OnDropPositionChanged(Models.DocumentNode dragged, Models.DocumentNode? target, Helpers.DropPosition pos, string description)
    {
        // 拖拽中显示：拖动节点 → 落点描述
        StatusText = $"拖动「{dragged.Title}」→ {description}";
    }

    /// <summary>
    /// 拖拽结束后恢复状态栏
    /// </summary>
    private void OnDragEnded()
    {
        // 不主动改 StatusText，让 OnNodeDropped/OnNodeDroppedToRoot 设置最终结果
        // 如果拖拽被取消（没触发 Drop），保留拖拽中的提示一秒后清空
        // 简化处理：只在拖拽结束时清空，让下一次操作的 StatusText 自然覆盖
    }

    /// <summary>
    /// 点击 TreeView 空白区域时取消选中
    /// </summary>
    private void OnBlankAreaClicked()
    {
        SelectedNode = null;
    }

    /// <summary>
    /// 外部文件拖入 TreeView 时处理（支持从资源管理器直接拖文件进来）
    /// </summary>
    private void OnExternalFilesDropped(string[] files, DocumentNode? targetFolder)
    {
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".html" or ".htm" or ".docx")
            {
                AddFileToNode(file, targetFolder);
            }
            else if (System.IO.Directory.Exists(file))
            {
                // 拖入的是文件夹，递归扫描
                var folderNode = new DocumentNode
                {
                    Title = System.IO.Path.GetFileName(file),
                    NodeType = NodeType.Folder,
                    Parent = targetFolder
                };
                AddFilesFromDirectory(file, folderNode);

                if (targetFolder == null)
                {
                    RootNodes.Add(folderNode);
                }
                else
                {
                    targetFolder.Children.Add(folderNode);
                    targetFolder.IsExpanded = true;
                }
            }
        }

        if (targetFolder != null) targetFolder.IsExpanded = true;
        RefreshPreview();
        StatusText = targetFolder == null
            ? $"已拖入 {files.Length} 项到根目录"
            : $"已拖入 {files.Length} 项到文件夹: {targetFolder.Title}";
    }

    /// <summary>
    /// 当 SelectedNode 变化时，自动监听新节点的 Title 等属性变化，触发右侧预览刷新
    /// </summary>
    partial void OnSelectedNodeChanged(DocumentNode? value)
    {
        // 防止递归调用导致栈溢出：
        // ClearAllSelection 会触发 IsSelected 变化 → TreeView.SelectedItemChanged
        // → 又会回写 SelectedNode → 再次进入此方法 → 死循环
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        try
        {
            // 取消旧节点的监听
            if (_currentListeningNode != null)
            {
                _currentListeningNode.PropertyChanged -= OnSelectedNodePropertyChanged;
            }

            // 清除其他节点的 IsSelected（跳过当前要选中的，避免触发不必要的属性变化）
            ClearAllSelection(RootNodes, value);

            // 设置新选中节点的 IsSelected（如果还不是 true）
            if (value != null && !value.IsSelected)
            {
                value.IsSelected = true;
            }

            // 监听新节点
            _currentListeningNode = value;
            if (_currentListeningNode != null)
            {
                _currentListeningNode.PropertyChanged += OnSelectedNodePropertyChanged;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private bool _isUpdatingSelection = false;

    /// <summary>
    /// 递归清除所有节点的 IsSelected，跳过指定节点
    /// </summary>
    private void ClearAllSelection(IEnumerable<DocumentNode> nodes, DocumentNode? except = null)
    {
        foreach (var node in nodes)
        {
            if (node != except && node.IsSelected)
            {
                node.IsSelected = false;
            }
            if (node.Children.Count > 0)
            {
                ClearAllSelection(node.Children, except);
            }
        }
    }

    /// <summary>
    /// 当选中节点的属性变化（如 Title）时，刷新右侧 CHM 目录预览
    /// </summary>
    private void OnSelectedNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Title 变化时影响目录树显示
        if (e.PropertyName == nameof(DocumentNode.Title) ||
            e.PropertyName == nameof(DocumentNode.Children))
        {
            RefreshPreview();
        }
    }

    private void OnNodeDropped(Models.DocumentNode draggedNode, Models.DocumentNode targetNode, Helpers.DropPosition position)
    {
        // 不能把自己拖到自己的子节点下（DragOver 已拦截，这里再防御一次）
        if (draggedNode == targetNode) return;
        if (IsAncestorOrSelf(draggedNode, targetNode)) return;

        switch (position)
        {
            case Helpers.DropPosition.Inside:
                // 仅文件夹可达（DragOver 已保证），作为 targetNode 的子节点插到末尾
                if (draggedNode.Parent == targetNode)
                {
                    // 同文件夹内 Inside → 等价于移到末尾
                    var idx = targetNode.Children.IndexOf(draggedNode);
                    if (idx >= 0 && idx != targetNode.Children.Count - 1)
                    {
                        targetNode.Children.RemoveAt(idx);
                        targetNode.Children.Add(draggedNode);
                    }
                    // 已经是末尾就不用动
                }
                else
                {
                    // 跨文件夹：先从原位置移除（避免同时存在于两个列表里）
                    RemoveFromCurrentParent(draggedNode);
                    targetNode.Children.Add(draggedNode);
                    draggedNode.Parent = targetNode;
                }
                targetNode.IsExpanded = true;
                break;

            case Helpers.DropPosition.Before:
                // 插到 targetNode 之前（同级）
                {
                    var newParent = targetNode.Parent;
                    var targetList = newParent?.Children ?? (System.Collections.IList)RootNodes;
                    var oldParent = draggedNode.Parent;
                    var sourceList = oldParent?.Children ?? (System.Collections.IList)RootNodes;
                    if (!ReferenceEquals(sourceList, targetList))
                    {
                        // 跨文件夹：先从原位置移除
                        sourceList.Remove(draggedNode);
                    }
                    InsertWithSameListFix(targetList, draggedNode, targetNode, before: true);
                    draggedNode.Parent = newParent;
                }
                break;

            case Helpers.DropPosition.After:
                // 插到 targetNode 之后（同级）
                {
                    var newParent = targetNode.Parent;
                    var targetList = newParent?.Children ?? (System.Collections.IList)RootNodes;
                    var oldParent = draggedNode.Parent;
                    var sourceList = oldParent?.Children ?? (System.Collections.IList)RootNodes;
                    if (!ReferenceEquals(sourceList, targetList))
                    {
                        // 跨文件夹：先从原位置移除
                        sourceList.Remove(draggedNode);
                    }
                    InsertWithSameListFix(targetList, draggedNode, targetNode, before: false);
                    draggedNode.Parent = newParent;
                }
                break;
        }

        // 拖拽后让被拖动的节点保持选中
        SelectedNode = draggedNode;

        RefreshPreview();
        StatusText = $"已移动: {draggedNode.Title}";
    }

    /// <summary>
    /// 在 targetList 中把 draggedNode 插到 targetNode 的前/后，
    /// 修复同列表"先 Remove 后 Insert"导致的索引偏移 Bug。
    ///
    /// 旧 Bug 复现（同列表）：
    ///   A B C D，拖 B 到 D 前（insertIndex=3）
    ///   Remove B → A C D（D 此时索引=2）
    ///   Insert at 3 → A C D B  ← 错！应该是 A C B D
    ///
    /// 修复：先算最终 index，再做一次性的 Remove + Insert。
    /// </summary>
    private static void InsertWithSameListFix(
        System.Collections.IList targetList,
        Models.DocumentNode draggedNode,
        Models.DocumentNode targetNode,
        bool before)
    {
        int targetIndex = targetList.IndexOf(targetNode);
        if (targetIndex < 0)
        {
            // 不该发生，防御性处理
            targetList.Add(draggedNode);
            return;
        }

        int finalIndex = before ? targetIndex : targetIndex + 1;

        // 如果 draggedNode 已经在 targetList 里，需要先移除并修正 finalIndex
        int oldIndex = targetList.IndexOf(draggedNode);
        if (oldIndex >= 0)
        {
            targetList.RemoveAt(oldIndex);
            // 移除后，如果 finalIndex 指向的位置因 draggedNode 被移除而前移，需要修正
            if (oldIndex < finalIndex)
            {
                finalIndex--;
            }
        }

        if (finalIndex < 0) finalIndex = 0;
        if (finalIndex > targetList.Count) finalIndex = targetList.Count;
        targetList.Insert(finalIndex, draggedNode);
    }

    private static bool IsAncestorOrSelf(Models.DocumentNode maybeAncestor, Models.DocumentNode node)
    {
        var p = node;
        while (p != null)
        {
            if (p == maybeAncestor) return true;
            p = p.Parent;
        }
        return false;
    }

    /// <summary>
    /// 把 draggedNode 从它当前所在的父列表（RootNodes 或 parent.Children）中移除，
    /// 用于跨文件夹拖拽时清理原位置。不移除 draggedNode.Parent 字段（由调用方维护）。
    /// </summary>
    private void RemoveFromCurrentParent(Models.DocumentNode draggedNode)
    {
        var oldParent = draggedNode.Parent;
        if (oldParent == null)
        {
            RootNodes.Remove(draggedNode);
        }
        else
        {
            oldParent.Children.Remove(draggedNode);
        }
    }

    /// <summary>
    /// 拖拽到 TreeView 真正空白处：把节点移到根末尾。
    /// 注意：只有鼠标确实落在 TreeView 没有节点的区域才会触发，
    /// 不会因为同列表前后移动误触发。
    /// </summary>
    private void OnNodeDroppedToRoot(Models.DocumentNode draggedNode)
    {
        // 已经在根了就不用动
        if (draggedNode.Parent == null) return;

        // 从原位置移除
        var oldParent = draggedNode.Parent;
        oldParent.Children.Remove(draggedNode);

        // 加到根末尾
        draggedNode.Parent = null;
        RootNodes.Add(draggedNode);

        SelectedNode = draggedNode;
        RefreshPreview();
        StatusText = $"已移到根目录: {draggedNode.Title}";
    }

    private void UpdateHhcStatus()
    {
        var p = HhcLocator.Find();
        HhcStatus = string.IsNullOrEmpty(p) ? "未找到 hhc.exe" : $"hhc.exe: {p}";
    }

    // ============== 命令 ==============

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 HTML / Word 文件",
            Filter = "支持的文件|*.html;*.htm;*.docx|HTML 文件|*.html;*.htm|Word 文档|*.docx|所有文件|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true) return;

        // 根据当前选中节点决定添加到哪个文件夹下
        var targetFolder = GetTargetFolderForAdd();

        LogManager.Instance.WriteOperation($"添加文件: {dlg.FileNames.Length} 个文件");

        foreach (var file in dlg.FileNames)
        {
            AddFileToNode(file, targetFolder);
            LogManager.Instance.WriteOperation($"  - {Path.GetFileName(file)}");
        }

        // 展开目标文件夹，让用户看到刚加入的文件
        if (targetFolder != null) targetFolder.IsExpanded = true;

        RefreshPreview();
        StatusText = targetFolder == null
            ? $"已添加 {dlg.FileNames.Length} 个文件到根目录"
            : $"已添加 {dlg.FileNames.Length} 个文件到文件夹: {targetFolder.Title}";
    }

    /// <summary>
    /// 根据当前选中节点，决定新添加的文件/文件夹应该放到哪里：
    /// - 没选：返回 null（根目录）
    /// - 选中文件夹：返回该文件夹
    /// - 选中文件：返回该文件所在的父文件夹
    /// </summary>
    private DocumentNode? GetTargetFolderForAdd()
    {
        if (SelectedNode == null) return null;
        return SelectedNode.IsFolder ? SelectedNode : SelectedNode.Parent;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择包含 HTML 文件的文件夹"
        };

        if (dlg.ShowDialog() != true) return;

        var folderPath = dlg.FolderName;
        var folderName = Path.GetFileName(folderPath);
        var folderNode = new DocumentNode
        {
            Title = folderName,
            NodeType = NodeType.Folder
        };

        // 根据当前选中决定父节点
        var targetFolder = GetTargetFolderForAdd();
        folderNode.Parent = targetFolder;

        // 递归扫描文件夹下的 HTML/Word
        AddFilesFromDirectory(folderPath, folderNode);

        if (targetFolder == null)
        {
            RootNodes.Add(folderNode);
        }
        else
        {
            targetFolder.Children.Add(folderNode);
            targetFolder.IsExpanded = true;
        }

        SelectedNode = folderNode;
        RefreshPreview();
        StatusText = targetFolder == null
            ? $"已添加文件夹到根目录: {folderName}"
            : $"已添加文件夹到 {targetFolder.Title}: {folderName}";
    }

    private void AddFilesFromDirectory(string dir, DocumentNode parentNode)
    {
        // HTML / Word 文件
        foreach (var file in Directory.GetFiles(dir).OrderBy(f => f))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".html" or ".htm" or ".docx")
            {
                AddFileToNode(file, parentNode);
            }
        }

        // 子文件夹
        foreach (var subDir in Directory.GetDirectories(dir).OrderBy(d => d))
        {
            var subFolder = new DocumentNode
            {
                Title = Path.GetFileName(subDir),
                NodeType = NodeType.Folder,
                Parent = parentNode
            };
            AddFilesFromDirectory(subDir, subFolder);
            parentNode.Children.Add(subFolder);
        }
    }

    private void AddFileToNode(string filePath, DocumentNode? parent)
    {
        var node = CreateFileNode(filePath, parent);
        if (parent == null)
        {
            RootNodes.Add(node);
        }
        else
        {
            parent.Children.Add(node);
        }
    }

    private DocumentNode CreateFileNode(string filePath, DocumentNode? parent)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var node = new DocumentNode
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            OriginalTitle = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = filePath,
            NodeType = ext == ".docx" ? NodeType.Word : NodeType.Html,
            Parent = parent
        };

        // 如果是 HTML，尝试读取 <title>
        if (node.NodeType == NodeType.Html)
        {
            var title = HtmlTitleExtractor.Extract(filePath);
            if (!string.IsNullOrEmpty(title))
            {
                node.Title = title;
                node.OriginalTitle = title;
            }
        }

        return node;
    }

    [RelayCommand]
    private void AddNewFolder()
    {
        // 根据当前选中决定父节点
        var targetFolder = GetTargetFolderForAdd();
        var folder = new DocumentNode
        {
            Title = "新建文件夹",
            NodeType = NodeType.Folder,
            Parent = targetFolder
        };

        if (targetFolder == null)
        {
            RootNodes.Add(folder);
        }
        else
        {
            targetFolder.Children.Add(folder);
            targetFolder.IsExpanded = true;
        }

        SelectedNode = folder;
        RefreshPreview();
        StatusText = targetFolder == null
            ? "已新建文件夹到根目录（双击重命名）"
            : $"已新建文件夹到 {targetFolder.Title}（双击重命名）";
    }

    [RelayCommand]
    private void AddChildFolder(DocumentNode? parent)
    {
        if (parent == null || !parent.IsFolder) return;
        var folder = new DocumentNode
        {
            Title = "新建文件夹",
            NodeType = NodeType.Folder,
            Parent = parent
        };
        parent.Children.Add(folder);
        parent.IsExpanded = true;
        SelectedNode = folder;
        RefreshPreview();
    }

    [RelayCommand]
    private void DeleteNode(DocumentNode? node)
    {
        if (node == null) return;

        LogManager.Instance.WriteOperation($"删除节点: {node.Title}");

        if (node.Parent == null)
        {
            RootNodes.Remove(node);
        }
        else
        {
            node.Parent.Children.Remove(node);
        }
        RefreshPreview();
        StatusText = $"已删除: {node.Title}";
    }

    [RelayCommand]
    private void MoveUp(DocumentNode? node)
    {
        if (node == null) return;
        var list = node.Parent?.Children ?? RootNodes;
        var idx = list.IndexOf(node);
        if (idx > 0)
        {
            list.Move(idx, idx - 1);
            RefreshPreview();
        }
    }

    [RelayCommand]
    private void MoveDown(DocumentNode? node)
    {
        if (node == null) return;
        var list = node.Parent?.Children ?? RootNodes;
        var idx = list.IndexOf(node);
        if (idx >= 0 && idx < list.Count - 1)
        {
            list.Move(idx, idx + 1);
            RefreshPreview();
        }
    }

    [RelayCommand]
    private async Task GenerateChmAsync()
    {
        if (RootNodes.Count == 0)
        {
            MessageBox.Show("请先添加文件或文件夹", "提示");
            return;
        }

        // 选择父目录，然后基于项目标题创建新文件夹
        var dlg = new OpenFolderDialog { Title = "选择 CHM 项目存放位置" };
        if (dlg.ShowDialog() != true) return;

        var parentDir = dlg.FolderName;

        // 生成安全的文件夹名（去除非法字符）
        var safeFolderName = SanitizeFolderName(ProjectTitle);
        if (string.IsNullOrWhiteSpace(safeFolderName))
        {
            safeFolderName = "CHM_Project";
        }

        // 如果文件夹已存在，添加序号
        var outputDir = Path.Combine(parentDir, safeFolderName);
        int counter = 1;
        while (Directory.Exists(outputDir))
        {
            outputDir = Path.Combine(parentDir, $"{safeFolderName}_{counter}");
            counter++;
        }

        // 创建新文件夹
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建输出文件夹失败：{ex.Message}", "错误");
            return;
        }

        var srcDir = Path.Combine(outputDir, "src");
        var tempDir = Path.Combine(Path.GetTempPath(), $"CHMGen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        IsBusy = true;
        ProgressValue = 0;
        AppendLog($"=== 开始生成 CHM: {ProjectTitle} ===");
        AppendLog($"输出目录: {outputDir}");
        AppendLog($"临时目录: {tempDir}");

        // 检查是否使用外部工具
        var config = ToolConfiguration.Instance;

        // 记录配置状态
        AppendLog($"- 配置检查:");
        AppendLog($"  UsePythonConverter = {config.UsePythonConverter}");
        AppendLog($"  PythonToolsPath = {config.PythonToolsPath}");
        AppendLog($"  IsPythonAvailable = {config.IsPythonAvailable()}");

        if (config.IsPythonAvailable())
        {
            AppendLog($"- 转换模式: 使用 Python doc2html");
        }
        else
        {
            AppendLog($"- 转换模式: 使用内置转换器 (OpenXmlPowerTools)");
        }

        try
        {
            // 1. 转换所有 Word 文件
            var wordNodes = RootNodes.SelectMany(r => r.GetAllFileNodes())
                .Where(n => n.NodeType == NodeType.Word)
                .ToList();

            if (wordNodes.Count > 0)
            {
                ProgressText = $"正在转换 Word 文件 (0/{wordNodes.Count})...";
                AppendLog($"- 转换 {wordNodes.Count} 个 Word 文件...");

                // 根据配置选择转换方式
                if (config.IsPythonAvailable())
                {
                    await ConvertWordsUsingPython(wordNodes, tempDir, config.PythonToolsPath);
                }
                else
                {
                    await ConvertWordsUsingBuiltIn(wordNodes, tempDir);
                }
            }
            else
            {
                AppendLog($"- 没有 Word 文件需要转换");
            }

            // 检查所有文件节点是否有效
            var allFileNodes = RootNodes.SelectMany(r => r.GetAllFileNodes()).ToList();
            AppendLog($"- 总文件数: {allFileNodes.Count}");

            int validCount = 0;
            int invalidCount = 0;
            foreach (var node in allFileNodes)
            {
                if (!string.IsNullOrEmpty(node.EffectiveHtmlPath) && File.Exists(node.EffectiveHtmlPath))
                {
                    validCount++;
                    AppendLog($"  ✓ {node.Title}: {node.EffectiveHtmlPath}");
                }
                else
                {
                    invalidCount++;
                    AppendLog($"  ✗ {node.Title}: HTML 路径无效或文件不存在 ({node.EffectiveHtmlPath})");
                }
            }

            AppendLog($"- 有效文件: {validCount}, 无效文件: {invalidCount}");

            if (validCount == 0)
            {
                MessageBox.Show("没有有效的 HTML 文件，无法生成 CHM。请确保：\n1. HTML 文件已添加且存在\n2. Word 文件已成功转换", "错误");
                return;
            }

            // 2. 确定默认首页
            var firstFile = allFileNodes.FirstOrDefault(n => !string.IsNullOrEmpty(n.EffectiveHtmlPath) && File.Exists(n.EffectiveHtmlPath));
            if (firstFile == null)
            {
                MessageBox.Show("没有可用的文件，无法生成 CHM", "错误");
                return;
            }
            var defaultTopic = firstFile.RelativePath;
            AppendLog($"- 默认首页: {defaultTopic}");

            // 3. 生成工程文件
            ProgressText = "正在生成工程文件...";
            ProgressValue = 60;
            AppendLog("- 生成 .hhp / .hhc / .hhk...");

            var project = _projectGenerator.Generate(
                outputDir, srcDir, ProjectTitle, defaultTopic, RootNodes,
                FullTextSearch, BinaryToc, AutoIndex);

            AppendLog($"  ✓ project.hhp");
            AppendLog($"  ✓ toc.hhc");
            AppendLog($"  ✓ index.hhk");

            // 4. 调用 hhc.exe 编译
            ProgressText = "正在调用 hhc.exe 编译...";
            ProgressValue = 80;

            var compileResult = await Task.Run(() => _compiler.Compile(project));
            ProgressValue = 100;
            ProgressText = "";

            if (compileResult.Success)
            {
                LastChmPath = compileResult.ChmPath;
                AppendLog($"=== 编译成功 ===");
                AppendLog($"输出: {compileResult.ChmPath}");
                AppendLog($"大小: {compileResult.ChmSizeBytes / 1024.0:F1} KB");

                StatusText = $"编译成功: {compileResult.ChmPath}";

                if (MessageBox.Show(
                    $"CHM 编译成功！\n\n路径: {compileResult.ChmPath}\n大小: {compileResult.ChmSizeBytes / 1024} KB\n\n是否打开所在文件夹？",
                    "成功", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{compileResult.ChmPath}\"");
                }
            }
            else
            {
                AppendLog($"=== 编译失败 ===");
                AppendLog($"错误: {compileResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(compileResult.OutputLog))
                    AppendLog("--- hhc.exe 输出 ---\n" + compileResult.OutputLog);

                MessageBox.Show($"编译失败：\n{compileResult.ErrorMessage}\n\n详见日志。",
                    "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText = "编译失败，请查看日志";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[异常] {ex.Message}");
            AppendLog($"[堆栈] {ex.StackTrace}");
            MessageBox.Show($"发生异常：{ex.Message}\n\n详见日志。", "错误");
            StatusText = "生成失败，请查看日志";
        }
        finally
        {
            IsBusy = false;
            // 清理临时目录
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [RelayCommand]
    private void LocateHhc()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 hhc.exe",
            Filter = "hhc.exe|hhc.exe|可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true)
        {
            // 把 hhc.exe 复制到程序目录
            try
            {
                var dest = Path.Combine(AppContext.BaseDirectory, "hhc.exe");
                if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(dlg.FileName, dest, overwrite: true);
                }
                HhcLocator.ResetCache();
                UpdateHhcStatus();
                MessageBox.Show("hhc.exe 已就位，可正常编译。", "成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制 hhc.exe 失败: {ex.Message}", "错误");
            }
        }
    }

    [RelayCommand]
    private void PreviewNode(DocumentNode? node)
    {
        if (node == null) return;
        if (node.IsFile && !string.IsNullOrEmpty(node.EffectiveHtmlPath) && File.Exists(node.EffectiveHtmlPath))
        {
            // 用系统默认程序打开预览
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = node.EffectiveHtmlPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开预览失败: {ex.Message}");
            }
        }
    }

    // ============== 辅助方法 ==============

    /// <summary>
    /// 清理文件夹名称，移除非法字符
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "CHM_Project";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString().Trim();

        // 折叠连续的下划线
        while (result.Contains("__"))
        {
            result = result.Replace("__", "_");
        }

        result = result.Trim('_');
        return string.IsNullOrEmpty(result) ? "CHM_Project" : result;
    }

    public void RefreshPreview()
    {
        PreviewItems.Clear();
        foreach (var root in RootNodes)
        {
            BuildPreview(root, 0);
        }
    }

    private void BuildPreview(DocumentNode node, int depth)
    {
        var icon = node.IsFolder ? "📁" : "📄";
        var indent = new string(' ', depth * 2);
        PreviewItems.Add(new PreviewItem
        {
            Title = $"{indent}{icon} {node.Title}",
            Path = node.IsFile ? node.RelativePath : "",
            IsFolder = node.IsFolder
        });

        foreach (var child in node.Children)
        {
            BuildPreview(child, depth + 1);
        }
    }

    private void AppendLog(string line)
    {
        LogText += line + Environment.NewLine;

        // 同时写入编译日志文件
        LogManager.Instance.WriteCompile(line);
    }

    /// <summary>
    /// 使用 Python doc2html 转换 Word 文件
    /// </summary>
    private async Task ConvertWordsUsingPython(List<DocumentNode> wordNodes, string tempDir, string pythonToolsPath)
    {
        int done = 0;
        var progress = new Progress<string>(msg => AppendLog($"    {msg}"));

        foreach (var wordNode in wordNodes)
        {
            try
            {
                var outputSubDir = Path.Combine(tempDir, $"word_{done + 1}");
                var htmlPath = await ExternalToolsIntegration.ConvertWordToPythonHtml(
                    pythonToolsPath,
                    wordNode.SourcePath,
                    outputSubDir,
                    progress);

                if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
                {
                    wordNode.ConvertedHtmlPath = htmlPath;
                    AppendLog($"  ✓ {Path.GetFileName(wordNode.SourcePath)} → {Path.GetFileName(htmlPath)}");
                }
                else
                {
                    AppendLog($"  ✗ {Path.GetFileName(wordNode.SourcePath)}: Python 转换失败，尝试使用内置转换器");
                    // 回退到内置转换器
                    try
                    {
                        var r = _wordConverter.ConvertToHtml(wordNode.SourcePath, tempDir, baseName: wordNode.Title);
                        wordNode.ConvertedHtmlPath = r.HtmlPath;
                        AppendLog($"  ✓ (内置) {Path.GetFileName(wordNode.SourcePath)} → {r.Title}");
                    }
                    catch (Exception ex2)
                    {
                        AppendLog($"  ✗ (内置) {Path.GetFileName(wordNode.SourcePath)}: {ex2.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"  ✗ {Path.GetFileName(wordNode.SourcePath)}: {ex.Message}");
            }

            done++;
            ProgressValue = done * 50.0 / wordNodes.Count;
            ProgressText = $"正在转换 Word 文件 ({done}/{wordNodes.Count})...";
            await Task.Yield();
        }
    }

    /// <summary>
    /// 使用内置转换器 (OpenXmlPowerTools) 转换 Word 文件
    /// </summary>
    private async Task ConvertWordsUsingBuiltIn(List<DocumentNode> wordNodes, string tempDir)
    {
        int done = 0;
        foreach (var wordNode in wordNodes)
        {
            try
            {
                var r = _wordConverter.ConvertToHtml(wordNode.SourcePath, tempDir,
                    baseName: wordNode.Title);
                wordNode.ConvertedHtmlPath = r.HtmlPath;
                if (!string.IsNullOrEmpty(r.Title) && r.Title != wordNode.Title)
                {
                    // 保留用户的 Title 编辑，不覆盖
                }
                AppendLog($"  ✓ {Path.GetFileName(wordNode.SourcePath)} → {r.Title}");
            }
            catch (Exception ex)
            {
                AppendLog($"  ✗ {Path.GetFileName(wordNode.SourcePath)}: {ex.Message}");
            }
            done++;
            ProgressValue = done * 50.0 / wordNodes.Count;
            ProgressText = $"正在转换 Word 文件 ({done}/{wordNodes.Count})...";
            await Task.Yield();
        }
    }
}

public class PreviewItem
{
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsFolder { get; set; }
}
