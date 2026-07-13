using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// hhc.exe 查找器
/// </summary>
[SupportedOSPlatform("windows")]
public static class HhcLocator
{
    private static string? _cached;

    /// <summary>
    /// 查找 hhc.exe，按顺序：
    /// 1. 程序当前目录
    /// 2. PATH 环境变量
    /// 3. HTML Help Workshop 默认安装路径
    /// </summary>
    public static string? Find()
    {
        if (_cached != null) return _cached;

        // 1. 当前目录
        var currentDir = Path.Combine(AppContext.BaseDirectory, "hhc.exe");
        if (File.Exists(currentDir)) { _cached = currentDir; return _cached; }

        // 2. PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var p = Path.Combine(dir, "hhc.exe");
                if (File.Exists(p)) { _cached = p; return _cached; }
            }
            catch { }
        }

        // 3. 默认安装路径
        string[] defaultPaths =
        {
            @"C:\Program Files (x86)\HTML Help Workshop\hhc.exe",
            @"C:\Program Files\HTML Help Workshop\hhc.exe",
            @"C:\Windows\Help\hhc.exe"
        };

        foreach (var p in defaultPaths)
        {
            if (File.Exists(p)) { _cached = p; return _cached; }
        }

        // 4. Windows Kits 通配路径
        var kitsBase = @"C:\Program Files (x86)\Windows Kits\10\bin";
        if (Directory.Exists(kitsBase))
        {
            foreach (var verDir in Directory.GetDirectories(kitsBase))
            {
                var x86Path = Path.Combine(verDir, "x86", "hhc.exe");
                if (File.Exists(x86Path)) { _cached = x86Path; return _cached; }
                var x64Path = Path.Combine(verDir, "x64", "hhc.exe");
                if (File.Exists(x64Path)) { _cached = x64Path; return _cached; }
            }
        }

        return null;
    }

    public static bool IsAvailable => Find() != null;

    public static void ResetCache() => _cached = null;
}

/// <summary>
/// CHM 工程文件生成器（.hhp / .hhc / .hhk）
/// </summary>
[SupportedOSPlatform("windows")]
public class ChmProjectGenerator
{
    /// <summary>
    /// 生成全部工程文件到指定目录
    /// </summary>
    /// <param name="outputDir">输出目录（src 上一级）</param>
    /// <param name="srcDir">src 目录（HTML 源所在）</param>
    /// <param name="title">CHM 标题</param>
    /// <param name="defaultTopic">默认首页（相对 src 路径，如 index.html）</param>
    /// <param name="rootNodes">文档树根节点列表</param>
    /// <param name="fullTextSearch">是否启用全文搜索</param>
    /// <param name="binaryToc">是否使用二进制 TOC</param>
    /// <param name="autoIndex">是否自动索引</param>
    /// <param name="wordNodeTxtMap">Word 节点到其 Python txt 配置文件的映射</param>
    public GeneratedProject Generate(string outputDir, string srcDir, string title,
        string defaultTopic, IReadOnlyList<Models.DocumentNode> rootNodes,
        bool fullTextSearch = true, bool binaryToc = true, bool autoIndex = true,
        Dictionary<Models.DocumentNode, string>? wordNodeTxtMap = null)
    {
        // 把所有文件复制到 src/ 下，按 RelativePath 摆放
        CopyFilesToSrc(srcDir, rootNodes);

        // 将 .hhp/.hhc/.hhk 生成到 src 目录中，这样 hhc.exe 可以在 src 目录中找到所有文件
        var hhpPath = Path.Combine(srcDir, "project.hhp");
        var hhcPath = Path.Combine(srcDir, "toc.hhc");
        var hhkPath = Path.Combine(srcDir, "index.hhk");

        // 收集所有文件节点
        var allFiles = rootNodes.SelectMany(r => r.GetAllFileNodes()).ToList();

        GenerateHhp(hhpPath, srcDir, title, defaultTopic, allFiles, fullTextSearch, binaryToc, autoIndex, wordNodeTxtMap, outputDir);
        GenerateHhc(hhcPath, srcDir, title, defaultTopic, rootNodes, binaryToc, wordNodeTxtMap);
        GenerateHhk(hhkPath, srcDir, allFiles, wordNodeTxtMap);

        return new GeneratedProject
        {
            HhpPath = hhpPath,
            HhcPath = hhcPath,
            HhkPath = hhkPath,
            SrcDir = srcDir,
            OutputDir = outputDir,
            ChmPath = Path.Combine(outputDir, $"{SanitizeFileName(title)}.chm")
        };
    }

    /// <summary>
    /// 将文档节点的文件复制到 src 目录，按照节点的 RelativePath 组织目录结构
    /// 对于 Word 节点，复制整个 Python 生成的目录结构
    /// 对于 API HTML 节点，复制单个文件并记录源目录用于复制共享资源
    /// 对于普通 HTML 文件，复制单个文件
    /// </summary>
    private void CopyFilesToSrc(string srcDir, IReadOnlyList<Models.DocumentNode> rootNodes)
    {
        if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir);

        // 记录所有处理过的 html 根目录，用于后续复制共享资源
        var htmlRootDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 记录所有 API HTML 源目录（用于复制 css/scripts）
        var apiHtmlSourceDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in rootNodes.SelectMany(r => r.GetAllFileNodes()))
        {
            // API HTML 节点：复制单个文件，记录源目录
            if (node.NodeType == Models.NodeType.ApiHtml && !string.IsNullOrEmpty(node.SourcePath))
            {
                var apiSourcePath = node.SourcePath;

                if (!File.Exists(apiSourcePath))
                {
                    System.Diagnostics.Debug.WriteLine($"警告: API HTML 文件不存在: {apiSourcePath}");
                    continue;
                }

                // 查找 ApiHtmlSourceDir（可能在当前节点或祖先节点）
                string? apiHtmlSourceDir = node.ApiHtmlSourceDir;
                if (string.IsNullOrEmpty(apiHtmlSourceDir))
                {
                    var ancestor = node.Parent;
                    while (ancestor != null && string.IsNullOrEmpty(apiHtmlSourceDir))
                    {
                        apiHtmlSourceDir = ancestor.ApiHtmlSourceDir;
                        ancestor = ancestor.Parent;
                    }
                }

                if (!string.IsNullOrEmpty(apiHtmlSourceDir))
                {
                    apiHtmlSourceDirs.Add(apiHtmlSourceDir);
                }

                // 计算目标路径
                var apiRelativePath = SafeHhcRelativePath(node.RelativePath);
                var apiDestPath = Path.Combine(srcDir, apiRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var apiDestDir = Path.GetDirectoryName(apiDestPath);
                if (!string.IsNullOrEmpty(apiDestDir)) Directory.CreateDirectory(apiDestDir);

                // 复制 HTML 文件
                try
                {
                    File.Copy(apiSourcePath, apiDestPath, overwrite: true);
                    System.Diagnostics.Debug.WriteLine($"复制 API HTML 文件: {apiSourcePath} → {apiDestPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"复制 API HTML 文件失败: {apiSourcePath} - {ex.Message}");
                    throw new Exception($"复制文件失败: {Path.GetFileName(apiSourcePath)} - {ex.Message}", ex);
                }

                continue; // 跳过后续逻辑
            }

            // Word 节点：只复制整个 Python 生成的目录结构，跳过单文件复制
            if (node.NodeType == Models.NodeType.Word && !string.IsNullOrEmpty(node.ConvertedHtmlPath))
            {
                var sourceFileDir = Path.GetDirectoryName(node.ConvertedHtmlPath);
                if (!string.IsNullOrEmpty(sourceFileDir))
                {
                    // 检查是否是 Python 生成的（在 html 目录下）
                    var outputDir = Path.GetDirectoryName(srcDir); // src 的父目录
                    var htmlDir = Path.Combine(outputDir ?? "", "html");

                    if (sourceFileDir.StartsWith(htmlDir, StringComparison.OrdinalIgnoreCase))
                    {
                        // Python 生成的文件，复制整个目录结构
                        // 找到文档的根目录（如 html/产品说明书/）
                        var relativePathFromHtml = sourceFileDir.Substring(htmlDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                        var firstSep = relativePathFromHtml.IndexOfAny(new[] { Path.DirectorySeparatorChar, '/' });
                        string docRootName;
                        if (firstSep > 0)
                        {
                            docRootName = relativePathFromHtml.Substring(0, firstSep);
                        }
                        else
                        {
                            docRootName = relativePathFromHtml;
                        }

                        var docRootDir = Path.Combine(htmlDir, docRootName);
                        if (Directory.Exists(docRootDir))
                        {
                            // 目标目录：src/{父路径}/{文档根名}
                            // 获取节点的父路径
                            var parentPathPrefix = GetNodePathPrefix(node);
                            var destDocRoot = string.IsNullOrEmpty(parentPathPrefix)
                                ? Path.Combine(srcDir, docRootName)
                                : Path.Combine(srcDir, parentPathPrefix.Replace('/', Path.DirectorySeparatorChar), docRootName);

                            // 递归复制整个文档目录
                            CopyDirectory(docRootDir, destDocRoot);
                            System.Diagnostics.Debug.WriteLine($"复制 Python 文档目录: {docRootDir} → {destDocRoot}");

                            // 记录 html 根目录，用于后续复制共享资源
                            htmlRootDirs.Add(htmlDir);
                        }
                    }
                }
                continue; // 跳过后续的单文件复制逻辑
            }

            // 普通 HTML 文件：单文件复制
            var sourcePath = node.EffectiveHtmlPath;

            // 检查源文件是否存在
            if (string.IsNullOrEmpty(sourcePath))
            {
                System.Diagnostics.Debug.WriteLine($"警告: 节点 {node.Title} 没有有效的 HTML 路径");
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                System.Diagnostics.Debug.WriteLine($"警告: 文件不存在: {sourcePath}");
                continue;
            }

            // 计算目标路径（相对 src 的 RelativePath，安全化文件名以兼容 hhc.exe）
            var relativePath = SafeHhcRelativePath(node.RelativePath);
            var destPath = Path.Combine(srcDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // 复制 HTML 文件
            try
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                System.Diagnostics.Debug.WriteLine($"复制文件: {sourcePath} → {destPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"复制文件失败: {sourcePath} - {ex.Message}");
                throw new Exception($"复制文件失败: {Path.GetFileName(sourcePath)} - {ex.Message}", ex);
            }
        }

        // 复制 Python 生成的共享资源目录（css, scripts, images 等）
        // 需要复制到每个 Word 节点的父路径下，以匹配 HTML 中的相对路径
        foreach (var htmlDir in htmlRootDirs)
        {
            if (Directory.Exists(htmlDir))
            {
                // 查找所有 Word 节点的父路径
                var wordNodes = rootNodes.SelectMany(r => r.GetAllFileNodes())
                    .Where(n => n.NodeType == Models.NodeType.Word)
                    .ToList();

                foreach (var wordNode in wordNodes)
                {
                    var parentPathPrefix = GetNodePathPrefix(wordNode);
                    var targetDir = string.IsNullOrEmpty(parentPathPrefix)
                        ? srcDir
                        : Path.Combine(srcDir, parentPathPrefix.Replace('/', Path.DirectorySeparatorChar));

                    // 查找并复制共享资源目录（css, scripts, images 等）
                    var sharedDirs = new[] { "css", "scripts", "images", "image", "fonts" };
                    foreach (var sharedDirName in sharedDirs)
                    {
                        var sharedSrcDir = Path.Combine(htmlDir, sharedDirName);
                        if (Directory.Exists(sharedSrcDir))
                        {
                            var sharedDestDir = Path.Combine(targetDir, sharedDirName);
                            CopyDirectory(sharedSrcDir, sharedDestDir);
                            System.Diagnostics.Debug.WriteLine($"复制共享资源目录: {sharedSrcDir} → {sharedDestDir}");
                        }
                    }
                }
            }
        }

        // 复制 API HTML 的共享资源目录（css, scripts 等）
        // 为每个 API HTML 目录组找到其在 src 中的根路径，将 css/scripts 复制到该路径下
        // 这样可以支持多个 API HTML 目录，避免资源文件互相覆盖
        foreach (var apiHtmlSourceDir in apiHtmlSourceDirs)
        {
            if (Directory.Exists(apiHtmlSourceDir))
            {
                System.Diagnostics.Debug.WriteLine($"处理 API HTML 源目录: {apiHtmlSourceDir}");

                // 找到使用这个源目录的所有 API HTML 节点
                var nodesFromThisSource = rootNodes.SelectMany(r => r.DescendantsAndSelf())
                    .Where(n => n.NodeType == Models.NodeType.ApiHtml &&
                                !string.IsNullOrEmpty(n.ApiHtmlSourceDir) &&
                                n.ApiHtmlSourceDir.Equals(apiHtmlSourceDir, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nodesFromThisSource.Count == 0)
                {
                    // 尝试从节点的祖先查找 ApiHtmlSourceDir
                    nodesFromThisSource = rootNodes.SelectMany(r => r.DescendantsAndSelf())
                        .Where(n => n.NodeType == Models.NodeType.ApiHtml &&
                                    FindApiHtmlSourceDir(n) == apiHtmlSourceDir)
                        .ToList();
                }

                if (nodesFromThisSource.Count > 0)
                {
                    // 找到这组节点的共同父路径（在 src 中的位置）
                    var firstNode = nodesFromThisSource[0];
                    var nodeParentPath = GetNodeParentPathInSrc(firstNode);

                    var targetBaseDir = string.IsNullOrEmpty(nodeParentPath)
                        ? srcDir
                        : Path.Combine(srcDir, nodeParentPath.Replace('/', Path.DirectorySeparatorChar));

                    System.Diagnostics.Debug.WriteLine($"  API HTML 节点父路径: {nodeParentPath}");
                    System.Diagnostics.Debug.WriteLine($"  目标根目录: {targetBaseDir}");

                    // 复制共享资源目录到目标根目录
                    var sharedDirs = new[] { "css", "scripts", "images", "image", "fonts" };
                    foreach (var sharedDirName in sharedDirs)
                    {
                        var sharedSrcDir = Path.Combine(apiHtmlSourceDir, sharedDirName);
                        if (Directory.Exists(sharedSrcDir))
                        {
                            var sharedDestDir = Path.Combine(targetBaseDir, sharedDirName);
                            CopyDirectory(sharedSrcDir, sharedDestDir);
                            System.Diagnostics.Debug.WriteLine($"  复制 API HTML 共享资源: {sharedSrcDir} → {sharedDestDir}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 查找节点或其祖先的 ApiHtmlSourceDir
    /// </summary>
    private static string? FindApiHtmlSourceDir(Models.DocumentNode node)
    {
        var current = node;
        while (current != null)
        {
            if (!string.IsNullOrEmpty(current.ApiHtmlSourceDir))
                return current.ApiHtmlSourceDir;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// 获取节点在 src 中的父路径（不包括节点自身）
    /// 例如：节点在树中的位置是 B/API/UserCode，返回 "B"
    /// </summary>
    private static string GetNodeParentPathInSrc(Models.DocumentNode node)
    {
        var parts = new List<string>();
        var current = node.Parent;

        while (current != null)
        {
            if (!string.IsNullOrEmpty(current.Title) && current.NodeType != Models.NodeType.ApiHtmlRoot)
            {
                parts.Insert(0, SanitizeFileName(current.Title));
            }
            current = current.Parent;
        }

        return string.Join("/", parts);
    }

    /// <summary>
    /// 复制 txt 配置文件中列出的 HTML 文件到 src 目录
    /// </summary>
    private void CopyTxtConfigFilesToSrc(string srcDir, string outputDir, List<TxtConfigParser.ConfigEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;

        var htmlDir = Path.Combine(outputDir, "html");
        if (!Directory.Exists(htmlDir))
        {
            System.Diagnostics.Debug.WriteLine($"警告: html 目录不存在: {htmlDir}");
            return;
        }

        foreach (var entry in entries)
        {
            // entry.RelativePath 格式如: "src/产品说明书/chapter_1/chapter_1.html"
            // 我们需要找到对应的源文件（在 html 目录下）
            // 源文件路径: html/产品说明书/chapter_1/chapter_1.html

            // 提取相对于 src 的路径部分（去掉 "src/" 前缀）
            var relPath = entry.RelativePath;
            if (relPath.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
            {
                relPath = relPath.Substring(4);
            }

            // 目标路径: src/{relPath}
            var destPath = Path.Combine(srcDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // 源文件路径: html/{去掉文档名前缀的路径}
            // 例如: src/产品说明书/chapter_1/chapter_1.html -> html/产品说明书/chapter_1/chapter_1.html
            var sourcePath = Path.Combine(htmlDir, relPath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(sourcePath))
            {
                try
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                    System.Diagnostics.Debug.WriteLine($"复制 txt 配置文件: {sourcePath} → {destPath}");

                    // 同时复制该 HTML 文件的 images 子目录（如果存在）
                    var sourceDir = Path.GetDirectoryName(sourcePath);
                    if (!string.IsNullOrEmpty(sourceDir))
                    {
                        var sourceImagesDir = Path.Combine(sourceDir, "images");
                        if (Directory.Exists(sourceImagesDir))
                        {
                            var destImagesDir = Path.Combine(destDir ?? srcDir, "images");
                            CopyDirectory(sourceImagesDir, destImagesDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"复制文件失败: {sourcePath} - {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"警告: 源文件不存在: {sourcePath}");
            }
        }
    }

    /// <summary>
    /// 递归复制目录
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// 生成 .hhp 项目文件（HTML Help Project）
    /// 包含 CHM 编译选项和文件列表
    /// </summary>
    /// <param name="hhpPath">.hhp 文件路径</param>
    /// <param name="srcDir">源文件目录</param>
    /// <param name="title">CHM 标题</param>
    /// <param name="defaultTopic">默认首页</param>
    /// <param name="allFiles">所有文件节点</param>
    /// <param name="fullTextSearch">是否启用全文搜索</param>
    /// <param name="binaryToc">是否使用二进制目录</param>
    /// <param name="autoIndex">是否自动索引</param>
    /// <param name="wordNodeTxtMap">Word 节点到 txt 配置文件的映射</param>
    /// <param name="outputDir">输出目录（用于计算 CHM 文件输出路径）</param>
    private void GenerateHhp(string hhpPath, string srcDir, string title, string defaultTopic,
        List<Models.DocumentNode> allFiles, bool fullTextSearch, bool binaryToc, bool autoIndex,
        Dictionary<Models.DocumentNode, string>? wordNodeTxtMap = null, string? outputDir = null)
    {
        var sb = new StringBuilder();

        // 如果 outputDir 不为空且不同于 srcDir，则 CHM 文件输出到父目录
        var chmFileName = SanitizeFileName(title) + ".chm";
        var compiledFile = (outputDir != null && outputDir != srcDir)
            ? $"../{chmFileName}"  // 输出到上级目录
            : chmFileName;         // 输出到当前目录

        sb.AppendLine("[OPTIONS]");
        sb.AppendLine("Compatibility=1.1 or later");
        sb.AppendLine($"Compiled file={compiledFile}");
        sb.AppendLine("Contents file=toc.hhc");
        sb.AppendLine("Index file=index.hhk");
        sb.AppendLine("Default Window=Main");
        sb.AppendLine($"Default topic={SafeHhcRelativePath(defaultTopic)}");
        sb.AppendLine("Display compile progress=Yes");
        sb.AppendLine($"Full-text search={(fullTextSearch ? "Yes" : "No")}");
        sb.AppendLine("Language=0x804");
        sb.AppendLine($"Title={title}");
        sb.AppendLine($"Binary TOC={(binaryToc ? "Yes" : "No")}");
        sb.AppendLine($"Auto Index={(autoIndex ? "Yes" : "No")}");
        sb.AppendLine();
        sb.AppendLine("[WINDOWS]");
        sb.AppendLine($"Main=\"{title}\",\"toc.hhc\",\"index.hhk\",\"{SafeHhcRelativePath(defaultTopic)}\",\"{SafeHhcRelativePath(defaultTopic)}\",,,,,0x63520,,0x387e,,,,,,0");
        sb.AppendLine();
        sb.AppendLine("[FILES]");

        // 添加来自文档树的文件（跳过 Word 节点，因为 Word 节点会在后面从 txt 配置中添加）
        foreach (var file in allFiles)
        {
            // 跳过 Word 节点，它们的文件列表从 Python txt 配置中获取
            if (file.NodeType == Models.NodeType.Word)
                continue;

            sb.AppendLine(SafeHhcRelativePath(file.RelativePath));
        }

        // 添加来自 Word 节点的 Python txt 配置的文件
        if (wordNodeTxtMap != null)
        {
            foreach (var kvp in wordNodeTxtMap)
            {
                var wordNode = kvp.Key;
                var txtFile = kvp.Value;
                if (File.Exists(txtFile))
                {
                    var entries = TxtConfigParser.Parse(txtFile, baseFolder: null, addPrefix: false);
                    var fullPathPrefix = GetWordNodeFullPathPrefix(wordNode);
                    foreach (var entry in entries)
                    {
                        var fullPath = string.IsNullOrEmpty(fullPathPrefix)
                            ? entry.RelativePath
                            : $"{fullPathPrefix}/{entry.RelativePath}";
                        sb.AppendLine(SafeHhcRelativePath(fullPath));
                    }
                }
            }
        }

        File.WriteAllBytes(hhpPath, Encoding.GetEncoding("GB2312").GetBytes(sb.ToString()));
    }

    /// <summary>
    /// 生成 .hhc 目录文件（HTML Help Contents）
    /// 定义 CHM 的树形目录结构
    /// </summary>
    /// <param name="hhcPath">.hhc 文件路径</param>
    /// <param name="srcDir">源文件目录</param>
    /// <param name="title">CHM 标题</param>
    /// <param name="defaultTopic">默认首页</param>
    /// <param name="rootNodes">文档树根节点</param>
    /// <param name="binaryToc">是否使用二进制目录</param>
    /// <param name="wordNodeTxtMap">Word 节点到 txt 配置文件的映射</param>
    private void GenerateHhc(string hhcPath, string srcDir, string title, string defaultTopic,
        IReadOnlyList<Models.DocumentNode> rootNodes, bool binaryToc,
        Dictionary<Models.DocumentNode, string>? wordNodeTxtMap = null)
    {
        // 调试：输出节点结构
        System.Diagnostics.Debug.WriteLine("=== GenerateHhc 节点结构 ===");
        System.Diagnostics.Debug.WriteLine($"Title: {title}");
        System.Diagnostics.Debug.WriteLine($"DefaultTopic: {defaultTopic}");
        System.Diagnostics.Debug.WriteLine($"RootNodes 数量: {rootNodes.Count}");
        for (int i = 0; i < rootNodes.Count; i++)
        {
            var node = rootNodes[i];
            System.Diagnostics.Debug.WriteLine($"RootNodes[{i}]:");
            PrintNodeTree(node, 1);
        }
        System.Diagnostics.Debug.WriteLine("=========================");

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\">");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta name=\"GENERATOR\" content=\"CHMGenerator WPF 2.0\">");
        sb.AppendLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=gb2312\">");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<object type=\"text/sitemap\">");
        sb.AppendLine("<param name=\"Font\" value=\"微软雅黑,9,0\">");
        sb.AppendLine("</object>");

        // 直接构建文档树，不添加首页节点
        // 这样目录树从实际的文档结构开始，而不是从"帮助文档"开始
        sb.AppendLine("<ul>");
        foreach (var node in rootNodes)
        {
            BuildHhcNode(sb, node, 1, wordNodeTxtMap);
        }
        sb.AppendLine("</ul>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllBytes(hhcPath, Encoding.GetEncoding("GB2312").GetBytes(sb.ToString()));
    }

    /// <summary>
    /// 调试：打印节点树结构
    /// </summary>
    private void PrintNodeTree(Models.DocumentNode node, int depth)
    {
        string indent = new string(' ', depth * 2);
        System.Diagnostics.Debug.WriteLine($"{indent}└─ {node.Title} (IsFolder={node.IsFolder})");
        foreach (var child in node.Children)
        {
            PrintNodeTree(child, depth + 1);
        }
    }

    /// <summary>
    /// 从 txt 配置构建 HHC 树节点
    /// </summary>
    private void BuildHhcFromTxtConfig(StringBuilder sb, List<TxtConfigParser.ConfigEntry> entries, int level)
    {
        // 构建父子关系映射
        var childrenMap = new Dictionary<string, List<TxtConfigParser.ConfigEntry>>();

        foreach (var entry in entries)
        {
            var parentKey = string.IsNullOrEmpty(entry.ParentPath) ? "" : entry.ParentPath;
            if (!childrenMap.ContainsKey(parentKey))
            {
                childrenMap[parentKey] = new List<TxtConfigParser.ConfigEntry>();
            }
            childrenMap[parentKey].Add(entry);
        }

        // 从根节点开始递归构建
        BuildHhcTreeNodes(sb, childrenMap, "", level);
    }

    /// <summary>
    /// 递归构建 HHC 树节点，带路径前缀
    /// </summary>
    private void BuildHhcTreeNodesWithPrefix(StringBuilder sb, Dictionary<string, List<TxtConfigParser.ConfigEntry>> childrenMap,
        string parentPath, int level, string pathPrefix)
    {
        string indent = new string(' ', level * 4);

        if (!childrenMap.ContainsKey(parentPath))
            return;

        foreach (var entry in childrenMap[parentPath])
        {
            // 添加路径前缀
            var fullPath = string.IsNullOrEmpty(pathPrefix)
                ? entry.RelativePath
                : $"{pathPrefix}/{entry.RelativePath}";

            System.Diagnostics.Debug.WriteLine($"    BuildHhcTreeNodesWithPrefix: Title={entry.Title}, entry.RelativePath={entry.RelativePath}, pathPrefix={pathPrefix}, fullPath={fullPath}");

            // 检查当前节点是否有子节点
            bool hasChildren = childrenMap.ContainsKey(entry.RelativePath);

            if (hasChildren)
            {
                // 有子节点的项，作为可展开的目录节点
                sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
                sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(entry.Title)}\">");
                sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(fullPath)}\">");
                sb.AppendLine($"{indent}</object>");
                sb.AppendLine($"{indent}<ul>");
                BuildHhcTreeNodesWithPrefix(sb, childrenMap, entry.RelativePath, level + 1, pathPrefix);
                sb.AppendLine($"{indent}</ul>");
                sb.AppendLine($"{indent}</li>");
            }
            else
            {
                // 没有子节点的项，作为普通节点
                sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
                sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(entry.Title)}\">");
                sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(fullPath)}\">");
                sb.AppendLine($"{indent}</object>");
                sb.AppendLine($"{indent}</li>");
            }
        }
    }

    /// <summary>
    /// 获取节点的路径前缀（父路径）
    /// </summary>
    private string GetNodePathPrefix(Models.DocumentNode node)
    {
        var parts = new List<string>();
        var current = node.Parent;
        while (current != null)
        {
            if (!string.IsNullOrEmpty(current.Title))
            {
                parts.Insert(0, SanitizeFileName(current.Title));
            }
            current = current.Parent;
        }
        return string.Join("/", parts);
    }

    /// <summary>
    /// 获取 Word 节点的完整路径前缀（父路径 + 文档目录名）
    /// </summary>
    private string GetWordNodeFullPathPrefix(Models.DocumentNode wordNode)
    {
        // 从 ConvertedHtmlPath 提取文档目录名
        // 例如：html/产品说明书/chapter_1/chapter_1.html → 产品说明书
        var docDirName = "";
        if (!string.IsNullOrEmpty(wordNode.ConvertedHtmlPath))
        {
            var htmlPath = wordNode.ConvertedHtmlPath;
            var dir = Path.GetDirectoryName(htmlPath);
            // 向上找到 html 目录的直接子目录
            while (!string.IsNullOrEmpty(dir))
            {
                var parentDir = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parentDir) &&
                    Path.GetFileName(parentDir).Equals("html", StringComparison.OrdinalIgnoreCase))
                {
                    docDirName = Path.GetFileName(dir);
                    break;
                }
                dir = parentDir;
            }
        }

        var parentPathPrefix = GetNodePathPrefix(wordNode);
        return string.IsNullOrEmpty(parentPathPrefix)
            ? docDirName
            : $"{parentPathPrefix}/{docDirName}";
    }
    private void BuildHhcTreeNodes(StringBuilder sb, Dictionary<string, List<TxtConfigParser.ConfigEntry>> childrenMap,
        string parentPath, int level)
    {
        string indent = new string(' ', level * 4);

        if (!childrenMap.ContainsKey(parentPath))
            return;

        foreach (var entry in childrenMap[parentPath])
        {
            // 检查当前节点是否有子节点
            bool hasChildren = childrenMap.ContainsKey(entry.RelativePath);

            if (hasChildren)
            {
                // 有子节点的项，作为可展开的目录节点
                sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
                sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(entry.Title)}\">");
                sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(entry.RelativePath)}\">");
                sb.AppendLine($"{indent}</object>");
                sb.AppendLine($"{indent}<ul>");
                BuildHhcTreeNodes(sb, childrenMap, entry.RelativePath, level + 1);
                sb.AppendLine($"{indent}</ul>");
                sb.AppendLine($"{indent}</li>");
            }
            else
            {
                // 没有子节点的项，作为普通节点
                sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
                sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(entry.Title)}\">");
                sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(entry.RelativePath)}\">");
                sb.AppendLine($"{indent}</object>");
                sb.AppendLine($"{indent}</li>");
            }
        }
    }

    /// <summary>
    /// 递归构建 .hhc 文件的节点结构
    /// 对于 Word 节点，展开 Python 生成的 txt 配置文件中的层级结构
    /// </summary>
    /// <param name="sb">字符串构建器</param>
    /// <param name="node">当前节点</param>
    /// <param name="level">当前层级（用于缩进）</param>
    /// <param name="wordNodeTxtMap">Word 节点到 txt 配置文件的映射</param>
    private void BuildHhcNode(StringBuilder sb, Models.DocumentNode node, int level,
        Dictionary<Models.DocumentNode, string>? wordNodeTxtMap = null)
    {
        string indent = new string(' ', level * 4);

        System.Diagnostics.Debug.WriteLine($"BuildHhcNode: level={level}, Title={node.Title}, IsFolder={node.IsFolder}, Children={node.Children.Count}");

        // API HTML 根节点：虚拟节点，不出现在 CHM 目录中，直接展开子节点
        if (node.NodeType == Models.NodeType.ApiHtmlRoot)
        {
            System.Diagnostics.Debug.WriteLine($"  → ApiHtmlRoot 虚拟节点，直接展开子节点");
            foreach (var child in node.Children)
            {
                BuildHhcNode(sb, child, level, wordNodeTxtMap);  // 注意：level 不增加
            }
            return;
        }

        if (node.IsFolder)
        {
            // 文件夹节点
            sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
            sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
            sb.AppendLine($"{indent}</object>");
            sb.AppendLine($"{indent}<ul>");
            foreach (var child in node.Children)
            {
                BuildHhcNode(sb, child, level + 1, wordNodeTxtMap);
            }
            sb.AppendLine($"{indent}</ul>");
            sb.AppendLine($"{indent}</li>");
        }
        else if (node.NodeType == Models.NodeType.ApiHtml && node.Children.Count > 0)
        {
            // API HTML 文件节点且有子节点：作为可展开的目录节点
            sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
            sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
            sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(node.RelativePath)}\">");
            sb.AppendLine($"{indent}</object>");
            sb.AppendLine($"{indent}<ul>");
            foreach (var child in node.Children)
            {
                BuildHhcNode(sb, child, level + 1, wordNodeTxtMap);
            }
            sb.AppendLine($"{indent}</ul>");
            sb.AppendLine($"{indent}</li>");

            System.Diagnostics.Debug.WriteLine($"  → API HTML 节点(有子节点): RelativePath={node.RelativePath}, Children={node.Children.Count}");
        }
        else if (node.NodeType == Models.NodeType.Word && wordNodeTxtMap != null && wordNodeTxtMap.ContainsKey(node))
        {
            // Word 节点：展开 Python txt 配置的层级
            var txtFile = wordNodeTxtMap[node];
            if (File.Exists(txtFile))
            {
                var entries = TxtConfigParser.Parse(txtFile, baseFolder: null, addPrefix: false);
                var fullPathPrefix = GetWordNodeFullPathPrefix(node);

                System.Diagnostics.Debug.WriteLine($"  → Word 节点，展开 txt: {txtFile}");
                System.Diagnostics.Debug.WriteLine($"     完整路径前缀: {fullPathPrefix}");

                // 构建父子关系映射
                var childrenMap = new Dictionary<string, List<TxtConfigParser.ConfigEntry>>();
                foreach (var entry in entries)
                {
                    var parentKey = string.IsNullOrEmpty(entry.ParentPath) ? "" : entry.ParentPath;
                    if (!childrenMap.ContainsKey(parentKey))
                    {
                        childrenMap[parentKey] = new List<TxtConfigParser.ConfigEntry>();
                    }
                    childrenMap[parentKey].Add(entry);
                }

                // 从根节点开始递归构建，添加路径前缀
                BuildHhcTreeNodesWithPrefix(sb, childrenMap, "", level, fullPathPrefix);
            }
            else
            {
                // txt 文件不存在，作为普通文件节点
                sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
                sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
                sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(node.RelativePath)}\">");
                sb.AppendLine($"{indent}</object>");
                sb.AppendLine($"{indent}</li>");

                System.Diagnostics.Debug.WriteLine($"  → Word 文件节点(无txt): RelativePath={node.RelativePath}");
            }
        }
        else
        {
            // 普通文件节点
            sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
            sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
            sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{SafeHhcRelativePath(node.RelativePath)}\">");
            sb.AppendLine($"{indent}</object>");
            sb.AppendLine($"{indent}</li>");

            System.Diagnostics.Debug.WriteLine($"  → 文件节点: RelativePath={node.RelativePath}");
        }
    }

    private void GenerateHhk(string hhkPath, string srcDir, List<Models.DocumentNode> allFiles,
        Dictionary<Models.DocumentNode, string>? wordNodeTxtMap = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\">");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta name=\"GENERATOR\" content=\"CHMGenerator WPF 2.0\">");
        sb.AppendLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=gb2312\">");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<ul>");

        // 收集所有条目并按标题排序
        var allEntries = new List<(string Title, string Path)>();

        // 添加文档树中的文件
        foreach (var file in allFiles)
        {
            allEntries.Add((file.Title, file.RelativePath));
        }

        // 添加 Word 节点的 Python txt 配置中的文件
        if (wordNodeTxtMap != null)
        {
            foreach (var kvp in wordNodeTxtMap)
            {
                var wordNode = kvp.Key;
                var txtFile = kvp.Value;
                if (File.Exists(txtFile))
                {
                    var entries = TxtConfigParser.Parse(txtFile, baseFolder: null, addPrefix: false);
                    var fullPathPrefix = GetWordNodeFullPathPrefix(wordNode);
                    foreach (var entry in entries)
                    {
                        var fullPath = string.IsNullOrEmpty(fullPathPrefix)
                            ? entry.RelativePath
                            : $"{fullPathPrefix}/{entry.RelativePath}";
                        allEntries.Add((entry.Title, fullPath));
                    }
                }
            }
        }

        // 按标题排序
        foreach (var entry in allEntries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"<li><object type=\"text/sitemap\">");
            sb.AppendLine($"  <param name=\"Name\" value=\"{EscapeXml(entry.Title)}\">");
            sb.AppendLine($"  <param name=\"Local\" value=\"{SafeHhcRelativePath(entry.Path)}\">");
            sb.AppendLine($"</object></li>");
        }

        sb.AppendLine("</ul>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllBytes(hhkPath, Encoding.GetEncoding("GB2312").GetBytes(sb.ToString()));
    }

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace("&", "&amp;")
                   .Replace("<", "&lt;")
                   .Replace(">", "&gt;")
                   .Replace("\"", "&quot;")
                   .Replace("'", "&apos;");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }

    /// <summary>
    /// 把文件名转换成 hhc.exe 能稳定编译的安全文件名。
    /// hhc.exe 4.74 对文件名兼容性极差，需要避免：
    ///   - 半角括号 () [] {}  ← 会被当作参数分隔符
    ///   - 空格                ← 路径解析时会被截断
    ///   - 多个点 .            ← V3.1.0 会让 hhc.exe 误判扩展名
    ///   - & + 等特殊字符      ← URL/path 解析问题
    /// 保留：中文字符、字母数字、下划线、连字符、单个点
    /// </summary>
    private static string SafeHhcFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "untitled";

        // 找到最后一个点的位置（作为扩展名分隔符保留）
        // 修复 v2.5 的 Bug：之前只保留第一个点，导致 .html 的点被替换成下划线
        int lastDotIndex = name.LastIndexOf('.');

        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == '.')
            {
                // 最后一个点保留（扩展名分隔符），其他点替换为下划线
                if (i == lastDotIndex && lastDotIndex > 0)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
                continue;
            }

            // hhc.exe 不友好的字符全部替换为下划线
            if (c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}')
            {
                sb.Append('_');
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                sb.Append('_');
                continue;
            }
            if (c == '&' || c == '+' || c == '#' || c == '%' || c == '!' || c == '@' ||
                c == '$' || c == '^' || c == '*' || c == '|' || c == ';' || c == ',' ||
                c == '\'' || c == '"' || c == '<' || c == '>' || c == '?' || c == '`' ||
                c == '=')
            {
                sb.Append('_');
                continue;
            }

            // 其他字符（中文、字母、数字、下划线、连字符）保留
            sb.Append(c);
        }

        // 折叠连续的下划线
        var result = sb.ToString();
        while (result.Contains("__"))
        {
            result = result.Replace("__", "_");
        }
        result = result.Trim('_');

        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }

    /// <summary>
    /// 把一个完整路径（含目录）的每个部分都安全化
    /// </summary>
    private static string SafeHhcRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;
        var parts = relativePath.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = SafeHhcFileName(parts[i]);
        }
        return string.Join("/", parts);
    }
}

/// <summary>
/// CHM 编译器，调用 hhc.exe
/// </summary>
[SupportedOSPlatform("windows")]
public class ChmCompiler
{
    public class CompileResult
    {
        public bool Success { get; set; }
        public string ChmPath { get; set; } = "";
        public long ChmSizeBytes { get; set; }
        public string OutputLog { get; set; } = "";
        public string ErrorLog { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public CompileResult Compile(GeneratedProject project, int timeoutMs = 300_000,
        CancellationToken cancellationToken = default)
    {
        var result = new CompileResult();
        var hhcPath = HhcLocator.Find();

        if (string.IsNullOrEmpty(hhcPath) || !File.Exists(hhcPath))
        {
            result.ErrorMessage = "未找到 hhc.exe，请安装 Microsoft HTML Help Workshop，或将 hhc.exe 放到程序目录下。";
            return result;
        }

        if (!File.Exists(project.HhpPath))
        {
            result.ErrorMessage = $"工程文件不存在: {project.HhpPath}";
            return result;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = hhcPath,
                Arguments = $"\"{project.HhpPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = project.SrcDir,  // 在 src 目录中运行，因为 .hhp 文件和 HTML 文件都在这里
                StandardOutputEncoding = Encoding.GetEncoding("GB2312"),
                StandardErrorEncoding = Encoding.GetEncoding("GB2312")
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 等待退出，支持取消
            while (!process.WaitForExit(500))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    result.ErrorMessage = "编译已被取消";
                    return result;
                }
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    result.ErrorMessage = $"编译超时（{timeoutMs / 1000} 秒）";
                    return result;
                }
                break;
            }

            process.WaitForExit();

            result.OutputLog = outputBuilder.ToString();
            result.ErrorLog = errorBuilder.ToString();
            result.ChmPath = project.ChmPath;

            if (File.Exists(project.ChmPath))
            {
                var fi = new FileInfo(project.ChmPath);
                result.ChmSizeBytes = fi.Length;
                result.Success = fi.Length > 0 && !ContainsFatalError(result.OutputLog);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "未生成 CHM 文件";
            }

            //// v2.8: 失败时收集诊断信息，附加到 OutputLog 让用户看到
            //if (!result.Success)
            //{
            //    // v2.12: 加 hhc.exe 环境全面诊断
            //    var envDiag = DiagnoseHhcEnvironment();
            //    result.OutputLog = envDiag + "\r\n\r\n" + result.OutputLog;


            //    var diagnostics = CollectDiagnostics(project, result.OutputLog);
            //    result.OutputLog = diagnostics + "\r\n\r\n" + result.OutputLog;
            //}
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static bool ContainsFatalError(string log)
    {
        // HHC5003 是路径错误，HHC4002 是编码错误，都是致命的
        return log.Contains("HHC5003") || log.Contains("HHC4002") ||
               log.Contains("Error:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 全面诊断 hhc.exe 环境
    /// </summary>
    public string DiagnoseHhcEnvironment()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== hhc.exe 环境全面诊断 ===");

        var hhcPath = HhcLocator.Find();
        sb.AppendLine($"1. hhc.exe 路径: {hhcPath ?? "<未找到>"}");

        if (string.IsNullOrEmpty(hhcPath))
        {
            sb.AppendLine("   ❌ 未找到 hhc.exe，请安装 HTML Help Workshop");
            return sb.ToString();
        }

        // 检查 hhc.exe 文件
        sb.AppendLine();
        sb.AppendLine("2. hhc.exe 文件信息:");
        try
        {
            var fi = new FileInfo(hhcPath);
            sb.AppendLine($"   路径: {fi.FullName}");
            sb.AppendLine($"   大小: {fi.Length} bytes");
            sb.AppendLine($"   修改时间: {fi.LastWriteTime}");
            sb.AppendLine($"   版本: {FileVersionInfo.GetVersionInfo(hhcPath).FileVersion ?? "<无>"}");
        }
        catch (Exception ex) { sb.AppendLine($"   读取失败: {ex.Message}"); }

        // 检查 Workshop 目录下的依赖文件
        sb.AppendLine();
        sb.AppendLine("3. HTML Help Workshop 目录文件:");
        var workshopDir = Path.GetDirectoryName(hhcPath) ?? "";
        var requiredFiles = new[] { "hhc.exe", "ha.dll", "hhdt.dll", "itcc.dll", "itircl.dll", "itss.dll", "hha.dll" };
        foreach (var f in requiredFiles)
        {
            var full = Path.Combine(workshopDir, f);
            var exists = File.Exists(full);
            sb.AppendLine($"   {f}: {(exists ? "✓ 存在" : "❌ 缺失")}");
        }

        // 检查系统目录下的 itss.dll 和 itircl.dll
        sb.AppendLine();
        sb.AppendLine("4. 系统 DLL 检查:");
        var systemDlls = new[] {
            @"C:\Windows\System32\itss.dll",
            @"C:\Windows\System32\itircl.dll",
            @"C:\Windows\System32\itcc.dll",
            @"C:\Windows\SysWOW64\itss.dll",
            @"C:\Windows\SysWOW64\itircl.dll",
            @"C:\Windows\SysWOW64\itcc.dll",
        };
        foreach (var dll in systemDlls)
        {
            var exists = File.Exists(dll);
            sb.AppendLine($"   {dll}: {(exists ? "✓" : "❌")}");
        }

        // 检查注册表里 itss.dll 是否注册
        sb.AppendLine();
        sb.AppendLine("5. 注册表检查 (itss.dll 注册状态):");
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("CLSID\\{4662DAE1-C9C3-101A-8676-040224009C02}");
            if (key != null)
            {
                sb.AppendLine("   ✓ itss.dll 已注册 (CLSID found)");
            }
            else
            {
                sb.AppendLine("   ❌ itss.dll 未注册！");
                sb.AppendLine("   → 请以管理员身份运行: regsvr32 C:\\Windows\\System32\\itss.dll");
            }
        }
        catch (Exception ex) { sb.AppendLine($"   检查失败: {ex.Message}"); }

        // 检查 itircl.dll 注册
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("CLSID\\{4662DAE2-C9C3-101A-8676-040224009C02}");
            if (key != null)
            {
                sb.AppendLine("   ✓ itircl.dll 已注册");
            }
            else
            {
                sb.AppendLine("   ❌ itircl.dll 未注册！");
                sb.AppendLine("   → 请以管理员身份运行: regsvr32 C:\\Windows\\System32\\itircl.dll");
            }
        }
        catch (Exception ex) { sb.AppendLine($"   检查失败: {ex.Message}"); }

        // 6. 推荐修复步骤
        sb.AppendLine();
        sb.AppendLine("6. 推荐修复步骤:");
        sb.AppendLine("   a) 以管理员身份运行 cmd，执行:");
        sb.AppendLine("      regsvr32 C:\\Windows\\System32\\itss.dll");
        sb.AppendLine("      regsvr32 C:\\Windows\\System32\\itircl.dll");
        sb.AppendLine("      regsvr32 C:\\Windows\\System32\\itcc.dll");
        sb.AppendLine("      regsvr32 C:\\Windows\\SysWOW64\\itss.dll");
        sb.AppendLine("      regsvr32 C:\\Windows\\SysWOW64\\itircl.dll");
        sb.AppendLine("      regsvr32 C:\\Windows\\SysWOW64\\itcc.dll");
        sb.AppendLine("   b) 重装 HTML Help Workshop 1.32:");
        sb.AppendLine("      https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/microsoft-html-help-downloads");
        sb.AppendLine("   c) 关闭 DEP（数据执行保护）对 hhc.exe:");
        sb.AppendLine("      系统属性 → 高级 → 性能 → DEP → 为 hhc.exe 关闭 DEP");
        sb.AppendLine("   d) 检查 360/安全软件是否拦截 hhc.exe");

        return sb.ToString();
    }

    /// <summary>
    /// 不区分大小写统计子字符串出现次数
    /// </summary>
    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return 0;
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }


    /// <summary>
    /// 编译失败时收集诊断信息，方便定位真正问题
    /// </summary>
    public string CollectDiagnostics(GeneratedProject project, string hhcOutputLog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 编译失败诊断信息 ===");

        // 1. 检查 hhp 文件
        if (File.Exists(project.HhpPath))
        {
            var fi = new FileInfo(project.HhpPath);
            sb.AppendLine($"project.hhp: 存在, 大小={fi.Length} bytes");
            // 输出 hhp 前 50 行
            sb.AppendLine("--- project.hhp 前 50 行 ---");
            try
            {
                var lines = File.ReadAllLines(project.HhpPath, Encoding.GetEncoding("GB2312"));
                for (int i = 0; i < Math.Min(50, lines.Length); i++)
                {
                    sb.AppendLine($"  {i + 1}: {lines[i]}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"  读取失败: {ex.Message}"); }
        }
        else
        {
            sb.AppendLine("project.hhp: 不存在！");
        }

        // 2. 检查 src 目录下的 HTML 文件
        sb.AppendLine();
        sb.AppendLine($"--- src 目录: {project.SrcDir} ---");
        if (Directory.Exists(project.SrcDir))
        {
            var htmlFiles = Directory.GetFiles(project.SrcDir, "*.html", SearchOption.AllDirectories);
            sb.AppendLine($"HTML 文件数: {htmlFiles.Length}");
            foreach (var f in htmlFiles.Take(5))
            {
                var fi = new FileInfo(f);
                var relPath = Path.GetRelativePath(project.SrcDir, f);
                sb.AppendLine($"  {relPath}: 大小={fi.Length} bytes");

                // 输出每个 HTML 文件前 500 字符
                try
                {
                    var content = File.ReadAllText(f, Encoding.GetEncoding("GB2312"));
                    var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                    sb.AppendLine($"  --- 前 500 字符 ---");
                    foreach (var line in preview.Split('\n').Take(15))
                    {
                        sb.AppendLine($"    {line.TrimEnd()}");
                    }
                }
                catch (Exception ex) { sb.AppendLine($"    读取失败: {ex.Message}"); }

                // v2.9: dump 文件前 64 字节十六进制，确认 BOM 和编码
                try
                {
                    var bytes = File.ReadAllBytes(f);
                    var hexPreview = bytes.Take(64).Select(b => b.ToString("X2")).Aggregate((a, b) => a + " " + b);
                    sb.AppendLine($"  --- 前 64 字节十六进制 ---");
                    sb.AppendLine($"    {hexPreview}");
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        sb.AppendLine($"    ⚠ 文件有 UTF-8 BOM！hhc.exe 可能不识别");
                    else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                        sb.AppendLine($"    ⚠ 文件有 UTF-16 LE BOM！hhc.exe 不识别");
                    else
                        sb.AppendLine($"    ✓ 无 BOM");
                }
                catch (Exception ex) { sb.AppendLine($"    字节读取失败: {ex.Message}"); }

                // v2.10: 扫描 hhc.exe 不友好的 HTML/CSS 模式
                try
                {
                    var fullContent = File.ReadAllText(f, Encoding.GetEncoding("GB2312"));
                    sb.AppendLine($"  --- hhc.exe 不友好模式扫描 ---");
                    sb.AppendLine($"    文件总长度: {fullContent.Length} 字符");
                    sb.AppendLine($"    data: URI 出现次数: {CountOccurrences(fullContent, "data:")}");
                    sb.AppendLine($"    base64 出现次数: {CountOccurrences(fullContent, "base64")}");
                    sb.AppendLine($"    <script 标签: {CountOccurrences(fullContent, "<script")}");
                    sb.AppendLine($"    <svg 标签: {CountOccurrences(fullContent, "<svg")}");
                    sb.AppendLine($"    <iframe 标签: {CountOccurrences(fullContent, "<iframe")}");
                    sb.AppendLine($"    <object 标签: {CountOccurrences(fullContent, "<object")}");
                    sb.AppendLine($"    CSS content: : {CountOccurrences(fullContent, "content:")}");
                    sb.AppendLine($"    CSS @media: {CountOccurrences(fullContent, "@media")}");
                    sb.AppendLine($"    CSS :hover: {CountOccurrences(fullContent, ":hover")}");
                    sb.AppendLine($"    CSS :first-child: {CountOccurrences(fullContent, ":first-child")}");
                    sb.AppendLine($"    CSS :nth-child: {CountOccurrences(fullContent, ":nth-child")}");
                    sb.AppendLine($"    background-image: : {CountOccurrences(fullContent, "background-image:")}");
                    sb.AppendLine($"    javascript: : {CountOccurrences(fullContent, "javascript:")}");
                    sb.AppendLine($"    <!-- 注释: {CountOccurrences(fullContent, "<!--")}");
                    sb.AppendLine($"    <col 标签: {CountOccurrences(fullContent, "<col")}");
                    sb.AppendLine($"    <colgroup 标签: {CountOccurrences(fullContent, "<colgroup")}");
                    sb.AppendLine($"    &nbsp;: {CountOccurrences(fullContent, "&nbsp;")}");
                    sb.AppendLine($"    &amp;: {CountOccurrences(fullContent, "&amp;")}");
                    sb.AppendLine($"    &lt;: {CountOccurrences(fullContent, "&lt;")}");

                    // 输出 HTML 完整内容（最多 8000 字符）
                    sb.AppendLine($"  --- HTML 完整内容 (前 8000 字符) ---");
                    var fullPreview = fullContent.Length > 8000 ? fullContent.Substring(0, 8000) + $"\r\n... (截断，总长 {fullContent.Length})" : fullContent;
                    sb.AppendLine(fullPreview);
                }
                catch (Exception ex) { sb.AppendLine($"    扫描失败: {ex.Message}"); }
            }
        }
        else
        {
            sb.AppendLine("src 目录不存在！");
        }

        // 3. 检查 CHM 输出目录是否可写
        sb.AppendLine();
        sb.AppendLine($"--- 输出目录: {project.OutputDir} ---");
        try
        {
            var testFile = Path.Combine(project.OutputDir, $"chmgen_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            sb.AppendLine("输出目录: 可写 ✓");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"输出目录: 不可写 ✗ - {ex.Message}");
        }

        // 4. 输出 hhc.exe 完整日志
        sb.AppendLine();
        sb.AppendLine("--- hhc.exe 完整输出 ---");
        sb.AppendLine(hhcOutputLog);

        return sb.ToString();
    }
}

public class GeneratedProject
{
    public string HhpPath { get; set; } = "";
    public string HhcPath { get; set; } = "";
    public string HhkPath { get; set; } = "";
    public string SrcDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string ChmPath { get; set; } = "";
}
