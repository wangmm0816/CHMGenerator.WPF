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

    /// <summary>文档树根节点列表</summary>
    public ObservableCollection<DocumentNode> RootNodes { get; } = new();

    private DocumentNode? _currentListeningNode;

    /// <summary>当前选中的节点</summary>
    [ObservableProperty] private DocumentNode? _selectedNode;

    /// <summary>右侧实时预览的扁平化条目</summary>
    public ObservableCollection<PreviewItem> PreviewItems { get; } = new();

    public MainViewModel()
    {
        UpdateHhcStatus();
        RootNodes.CollectionChanged += (_, _) => RefreshPreview();
        Helpers.TreeViewDragDropHelper.NodeDropped += OnNodeDropped;
    }

    /// <summary>
    /// 当 SelectedNode 变化时，自动监听新节点的 Title 等属性变化，触发右侧预览刷新
    /// </summary>
    partial void OnSelectedNodeChanged(DocumentNode? value)
    {
        // 取消旧节点的监听
        if (_currentListeningNode != null)
        {
            _currentListeningNode.PropertyChanged -= OnSelectedNodePropertyChanged;
        }

        // 监听新节点
        _currentListeningNode = value;
        if (_currentListeningNode != null)
        {
            _currentListeningNode.PropertyChanged += OnSelectedNodePropertyChanged;
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

    private void OnNodeDropped(Models.DocumentNode draggedNode, Models.DocumentNode targetNode)
    {
        // 不能把自己拖到自己的子节点下
        if (draggedNode == targetNode) return;

        // 决定放置方式：
        // - 如果 targetNode 是文件夹且展开 → 作为 targetNode 的第一个子节点
        // - 如果 targetNode 是文件夹且折叠 → 作为 targetNode 的子节点
        // - 如果 targetNode 是文件 → 插入到 targetNode 之前（同级）
        System.Collections.IList targetList;
        Models.DocumentNode? newParent;
        int insertIndex;

        if (targetNode.IsFolder)
        {
            // 拖到文件夹下
            targetList = targetNode.Children;
            newParent = targetNode;
            insertIndex = 0;
        }
        else
        {
            // 拖到文件之前（同级）
            newParent = targetNode.Parent;
            targetList = newParent?.Children ?? (System.Collections.IList)RootNodes;
            insertIndex = targetList.IndexOf(targetNode);
        }

        // 从原位置移除
        var oldParent = draggedNode.Parent;
        var oldList = oldParent?.Children ?? (System.Collections.IList)RootNodes;
        oldList.Remove(draggedNode);

        // 插入到新位置
        if (insertIndex > targetList.Count) insertIndex = targetList.Count;
        targetList.Insert(insertIndex, draggedNode);
        draggedNode.Parent = newParent;

        if (targetNode.IsFolder) targetNode.IsExpanded = true;

        RefreshPreview();
        StatusText = $"已移动: {draggedNode.Title}";
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

        foreach (var file in dlg.FileNames)
        {
            AddFileToRoot(file);
        }
        RefreshPreview();
        StatusText = $"已添加 {dlg.FileNames.Length} 个文件";
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
        folderNode.Parent = null;

        // 递归扫描文件夹下的 HTML/Word
        AddFilesFromDirectory(folderPath, folderNode);

        RootNodes.Add(folderNode);
        RefreshPreview();
        StatusText = $"已添加文件夹: {folderName}";
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

    private void AddFileToRoot(string filePath)
    {
        var node = CreateFileNode(filePath, null);
        RootNodes.Add(node);
    }

    private void AddFileToNode(string filePath, DocumentNode parent)
    {
        var node = CreateFileNode(filePath, parent);
        parent.Children.Add(node);
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
        var folder = new DocumentNode
        {
            Title = "新建文件夹",
            NodeType = NodeType.Folder,
            Parent = null
        };
        RootNodes.Add(folder);
        SelectedNode = folder;
        RefreshPreview();
        StatusText = "已新建文件夹（双击重命名）";
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

        // 选择输出目录
        var dlg = new OpenFolderDialog { Title = "选择 CHM 输出目录" };
        if (dlg.ShowDialog() != true) return;

        var outputDir = dlg.FolderName;
        var srcDir = Path.Combine(outputDir, "src");
        var tempDir = Path.Combine(Path.GetTempPath(), $"CHMGen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        IsBusy = true;
        ProgressValue = 0;
        AppendLog($"=== 开始生成 CHM: {ProjectTitle} ===");

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

            // 2. 确定默认首页
            var firstFile = RootNodes.SelectMany(r => r.GetAllFileNodes()).FirstOrDefault();
            if (firstFile == null)
            {
                MessageBox.Show("没有可用的文件，无法生成 CHM", "错误");
                return;
            }
            var defaultTopic = firstFile.RelativePath;

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
            MessageBox.Show($"发生异常：{ex.Message}", "错误");
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
    }
}

public class PreviewItem
{
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsFolder { get; set; }
}
