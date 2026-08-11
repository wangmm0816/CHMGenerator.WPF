using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

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
public partial class ChmProjectGenerator
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
        CopyFilesToSrc_Refactored(srcDir, rootNodes);

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
    /// 旧的文件复制逻辑（已废弃，保留用于参考）
    /// </summary>
    [Obsolete("已由 CopyFilesToSrc_Refactored 替代")]
    private void CopyFilesToSrc_Old(string srcDir, IReadOnlyList<Models.DocumentNode> rootNodes)
    {
        if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir);

        // 记录所有处理过的 html 根目录，用于后续复制共享资源
        var htmlRootDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 记录所有 API HTML 源目录（用于复制 css/scripts）
        var apiHtmlSourceDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 为每个 API HTML 源目录建立全局的文件名映射表（原始文件名 -> 安全化文件名）
        // key: API HTML 源目录路径, value: 文件名映射字典
        var globalHtmlFileNameMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // 缓存已扫描的源目录及其所有文件列表，避免重复扫描
        var sourceDirectoryCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 单次遍历：收集所有 API HTML 源目录并建立全局文件名映射
        foreach (var node in rootNodes.SelectMany(r => r.GetAllFileNodes()))
        {
            if (node.NodeType == Models.NodeType.ApiHtml && !string.IsNullOrEmpty(node.SourcePath))
            {
                // 查找 ApiHtmlSourceDir
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

                if (!string.IsNullOrEmpty(apiHtmlSourceDir) && !globalHtmlFileNameMaps.ContainsKey(apiHtmlSourceDir))
                {
                    apiHtmlSourceDirs.Add(apiHtmlSourceDir);

                    // 扫描整个源目录树，建立文件名映射（只扫描一次并缓存）
                    List<string> allHtmlFiles;
                    if (sourceDirectoryCache.ContainsKey(apiHtmlSourceDir))
                    {
                        allHtmlFiles = sourceDirectoryCache[apiHtmlSourceDir];
                    }
                    else
                    {
                        allHtmlFiles = Directory.GetFiles(apiHtmlSourceDir, "*.html", SearchOption.AllDirectories).ToList();
                        sourceDirectoryCache[apiHtmlSourceDir] = allHtmlFiles;
                        System.Diagnostics.Debug.WriteLine($"[目录扫描] {apiHtmlSourceDir}: 找到 {allHtmlFiles.Count} 个 HTML 文件");
                    }

                    var fileNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var htmlFile in allHtmlFiles)
                    {
                        var originalFileName = Path.GetFileName(htmlFile);
                        var safeFileName = SafeHhcFileName(originalFileName);

                        if (!originalFileName.Equals(safeFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            fileNameMap[originalFileName] = safeFileName;
                        }
                    }

                    globalHtmlFileNameMaps[apiHtmlSourceDir] = fileNameMap;
                    System.Diagnostics.Debug.WriteLine($"[全局映射] {apiHtmlSourceDir}: {fileNameMap.Count} 个文件需要重命名");
                }
            }
        }

        // 复制文件并修复链接
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

                // 获取该源目录的全局 HTML 文件名映射表
                Dictionary<string, string>? htmlFileNameMap = null;
                if (!string.IsNullOrEmpty(apiHtmlSourceDir) && globalHtmlFileNameMaps.ContainsKey(apiHtmlSourceDir))
                {
                    htmlFileNameMap = globalHtmlFileNameMaps[apiHtmlSourceDir];
                }

                // 计算目标路径
                var apiRelativePath = SafeHhcRelativePath(node.RelativePath);
                var apiDestPath = Path.Combine(srcDir, apiRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var apiDestDir = Path.GetDirectoryName(apiDestPath);
                if (!string.IsNullOrEmpty(apiDestDir)) Directory.CreateDirectory(apiDestDir);

                // 复制同目录下的其他文件（如 PDF、图片等），并重命名为安全的文件名
                var apiSourceDir = Path.GetDirectoryName(apiSourcePath);
                var fileNameMap = new Dictionary<string, string>(); // 原始文件名 -> 新文件名（用于 PDF 等附件）
                var dirNameMap = new Dictionary<string, string>(); // 原始目录名 -> 安全化目录名

                // 1. 建立子目录名映射（基于 apiHtmlSourceDir，因为 HTML 链接是相对于这个根目录的）
                if (!string.IsNullOrEmpty(apiHtmlSourceDir) && Directory.Exists(apiHtmlSourceDir))
                {
                    try
                    {
                        var subDirs = Directory.GetDirectories(apiHtmlSourceDir, "*", SearchOption.AllDirectories);
                        System.Diagnostics.Debug.WriteLine($"  扫描 {apiHtmlSourceDir}，找到 {subDirs.Length} 个子目录");

                        foreach (var subDir in subDirs)
                        {
                            var originalDirName = Path.GetFileName(subDir);
                            var safeDirName = SafeHhcFileName(originalDirName);
                            if (!originalDirName.Equals(safeDirName, StringComparison.OrdinalIgnoreCase))
                            {
                                dirNameMap[originalDirName] = safeDirName;
                                System.Diagnostics.Debug.WriteLine($"    目录映射: {originalDirName} → {safeDirName}");
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"  建立了 {dirNameMap.Count} 个目录名映射（基于 {apiHtmlSourceDir}）");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"扫描子目录失败: {ex.Message}");
                    }
                }

                // 2. 复制同目录下的其他文件（如 PDF、图片等）
                if (!string.IsNullOrEmpty(apiSourceDir) && Directory.Exists(apiSourceDir))
                {
                    try
                    {

                        // 2. 获取同目录下的所有非 HTML 文件
                        var siblingFiles = Directory.GetFiles(apiSourceDir)
                            .Where(f => !f.Equals(apiSourcePath, StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(Path.GetExtension(f), ".html", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        int fileIndex = 1;
                        foreach (var siblingFile in siblingFiles)
                        {
                            var siblingFileName = Path.GetFileName(siblingFile);
                            var extension = Path.GetExtension(siblingFileName);
                            var baseName = Path.GetFileNameWithoutExtension(siblingFileName);
                            string targetFileName = siblingFileName;

                            // 检查文件名是否包含非 ASCII 字符（如中文）
                            if (ContainsNonAscii(siblingFileName))
                            {
                                // 生成安全的 ASCII 文件名：保留扩展名，使用序号
                                targetFileName = $"attachment_{fileIndex:D3}{extension}";
                                fileNameMap[siblingFileName] = targetFileName;
                                fileIndex++;
                                System.Diagnostics.Debug.WriteLine($"  重命名非 ASCII 文件: {siblingFileName} → {targetFileName}");
                            }

                            var siblingDestPath = Path.Combine(apiDestDir!, targetFileName);
                            File.Copy(siblingFile, siblingDestPath, overwrite: true);
                            System.Diagnostics.Debug.WriteLine($"  复制同目录文件: {siblingFile} → {siblingDestPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"复制同目录文件失败: {ex.Message}");
                        // 不抛出异常，继续处理其他文件
                    }
                }

                // 复制 HTML 文件（需要时才读取内容进行修复）
                try
                {
                    bool needsContentModification = (htmlFileNameMap != null && htmlFileNameMap.Count > 0) || fileNameMap.Count > 0 || dirNameMap.Count > 0;

                    if (!needsContentModification)
                    {
                        // 没有需要修复的链接，直接复制文件（快速路径）
                        File.Copy(apiSourcePath, apiDestPath, overwrite: true);
                        System.Diagnostics.Debug.WriteLine($"快速复制 API HTML 文件: {apiSourcePath} → {apiDestPath}");
                    }
                    else
                    {
                        // 需要修复链接，读取并处理内容
                        var htmlContent = File.ReadAllText(apiSourcePath, Encoding.GetEncoding("GB2312"));

                        // 解码 title 标签中的实体编码
                        htmlContent = System.Text.RegularExpressions.Regex.Replace(
                            htmlContent,
                            @"<title\b[^>]*>(.*?)</title>",
                            match =>
                            {
                                var titleContent = match.Groups[1].Value;
                                var decodedTitle = System.Net.WebUtility.HtmlDecode(titleContent);
                                return $"<title>{decodedTitle}</title>";
                            },
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline
                        );

                        // 修复 HTML 内部链接
                        if (htmlFileNameMap != null && htmlFileNameMap.Count > 0)
                        {
                            // 优化：一次性替换所有文件名，避免多次正则匹配
                            // 构建一个联合正则表达式：(file1.html|file2.html|file3.html)
                            var escapedFileNames = htmlFileNameMap.Keys.Select(Regex.Escape).ToList();
                            var combinedPattern = $@"(href\s*=\s*[""'])([^""']*/)?((?:{string.Join("|", escapedFileNames)}))([""'])";

                            htmlContent = Regex.Replace(
                                htmlContent,
                                combinedPattern,
                                m =>
                                {
                                    var originalFileName = m.Groups[3].Value;
                                    if (htmlFileNameMap.TryGetValue(originalFileName, out var safeFileName))
                                    {
                                        return $"{m.Groups[1].Value}{m.Groups[2].Value}{safeFileName}{m.Groups[4].Value}";
                                    }
                                    return m.Value; // 不应该发生
                                },
                                RegexOptions.IgnoreCase
                            );

                            System.Diagnostics.Debug.WriteLine($"  批量修复了 {htmlFileNameMap.Count} 个 HTML 文件链接");
                        }

                        // 修复所有 href 链接：同时处理目录名和文件名的替换
                        // 使用正则表达式匹配所有 href 属性，并检查路径中是否包含需要替换的目录名或文件名
                        if (dirNameMap.Count > 0 || htmlFileNameMap != null && htmlFileNameMap.Count > 0 || fileNameMap.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"  开始修复链接 - 目录映射数: {dirNameMap.Count}, HTML文件映射数: {htmlFileNameMap?.Count ?? 0}, 附件映射数: {fileNameMap.Count}");

                            var hrefPattern = @"(href\s*=\s*[""'])([^""']+)([""'])";
                            int replacementCount = 0;

                            htmlContent = Regex.Replace(htmlContent, hrefPattern, match =>
                            {
                                var quote = match.Groups[1].Value;  // href=" 或 href='
                                var path = match.Groups[2].Value;   // 链接路径
                                var endQuote = match.Groups[3].Value;  // " 或 '

                                // 跳过绝对路径和特殊协议
                                if (path.StartsWith("http://") || path.StartsWith("https://") ||
                                    path.StartsWith("//") || path.StartsWith("#") || path.StartsWith("javascript:"))
                                {
                                    return match.Value;
                                }

                                var originalPath = path;
                                var modifiedPath = path;

                                // 1. 替换路径中的目录名（支持多级路径）
                                if (dirNameMap.Count > 0)
                                {
                                    foreach (var kvp in dirNameMap)
                                    {
                                        var originalDirName = kvp.Key;
                                        var safeDirName = kvp.Value;

                                        // 匹配独立的目录名（前后是 / 或路径边界）
                                        var dirPattern = $@"(^|/){Regex.Escape(originalDirName)}(/|$)";
                                        if (Regex.IsMatch(modifiedPath, dirPattern))
                                        {
                                            var newPath = Regex.Replace(modifiedPath, dirPattern, $"$1{safeDirName}$2");
                                            System.Diagnostics.Debug.WriteLine($"    替换目录: {modifiedPath} → {newPath}");
                                            modifiedPath = newPath;
                                        }
                                    }
                                }

                                // 2. 替换路径中的 HTML 文件名
                                if (htmlFileNameMap != null && htmlFileNameMap.Count > 0)
                                {
                                    foreach (var kvp in htmlFileNameMap)
                                    {
                                        var originalFileName = kvp.Key;
                                        var safeFileName = kvp.Value;

                                        // 只替换路径末尾的文件名
                                        if (modifiedPath.EndsWith(originalFileName))
                                        {
                                            modifiedPath = modifiedPath.Substring(0, modifiedPath.Length - originalFileName.Length) + safeFileName;
                                            System.Diagnostics.Debug.WriteLine($"    替换文件: {originalPath} → {modifiedPath}");
                                        }
                                    }
                                }

                                // 3. 替换附件文件名（PDF、图片等）
                                if (fileNameMap.Count > 0)
                                {
                                    foreach (var kvp in fileNameMap)
                                    {
                                        var originalFileName = kvp.Key;
                                        var safeFileName = kvp.Value;

                                        if (modifiedPath.EndsWith(originalFileName) || modifiedPath == originalFileName)
                                        {
                                            modifiedPath = modifiedPath.Substring(0, modifiedPath.Length - originalFileName.Length) + safeFileName;
                                        }
                                    }
                                }

                                if (originalPath != modifiedPath)
                                {
                                    replacementCount++;
                                }

                                return $"{quote}{modifiedPath}{endQuote}";
                            }, RegexOptions.IgnoreCase);

                            System.Diagnostics.Debug.WriteLine($"  完成链接修复：替换了 {replacementCount} 个链接");
                        }

                        // 写入目标文件
                        File.WriteAllText(apiDestPath, htmlContent, Encoding.GetEncoding("GB2312"));
                        System.Diagnostics.Debug.WriteLine($"复制并修复 API HTML 文件: {apiSourcePath} → {apiDestPath}");
                    }
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
                            // 安全化目录名，避免 hhc.exe 编译错误
                            var safeDocRootName = SafeHhcFileName(docRootName);

                            // 目标目录：src/{父路径}/{安全化的文档根名}
                            // 获取节点的父路径
                            var parentPathPrefix = GetNodePathPrefix(node);
                            var destDocRoot = string.IsNullOrEmpty(parentPathPrefix)
                                ? Path.Combine(srcDir, safeDocRootName)
                                : Path.Combine(srcDir, parentPathPrefix.Replace('/', Path.DirectorySeparatorChar), safeDocRootName);

                            // 为 Word HTML 目录建立文件名映射（使用缓存）
                            var wordHtmlFileNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            List<string> wordHtmlFiles;

                            if (sourceDirectoryCache.ContainsKey(docRootDir))
                            {
                                wordHtmlFiles = sourceDirectoryCache[docRootDir];
                            }
                            else
                            {
                                wordHtmlFiles = Directory.GetFiles(docRootDir, "*.html", SearchOption.AllDirectories).ToList();
                                sourceDirectoryCache[docRootDir] = wordHtmlFiles;
                                System.Diagnostics.Debug.WriteLine($"[目录扫描] Word HTML: {docRootDir}: 找到 {wordHtmlFiles.Count} 个文件");
                            }

                            foreach (var htmlFile in wordHtmlFiles)
                            {
                                var originalFileName = Path.GetFileName(htmlFile);
                                var safeFileName = SafeHhcFileName(originalFileName);

                                if (!originalFileName.Equals(safeFileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    wordHtmlFileNameMap[originalFileName] = safeFileName;
                                }
                            }

                            if (wordHtmlFileNameMap.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Word HTML 映射] {docRootDir}: {wordHtmlFileNameMap.Count} 个文件需要重命名");
                            }

                            // 递归复制整个文档目录，并修复 HTML 链接
                            CopyDirectory(docRootDir, destDocRoot, wordHtmlFileNameMap);
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

            // 复制 HTML 文件并修正 title 中的实体编码
            try
            {
                // 读取 HTML 内容
                var htmlContent = File.ReadAllText(sourcePath, Encoding.GetEncoding("GB2312"));

                // 查找并解码 title 标签中的实体编码
                htmlContent = System.Text.RegularExpressions.Regex.Replace(
                    htmlContent,
                    @"<title\b[^>]*>(.*?)</title>",
                    match =>
                    {
                        var titleContent = match.Groups[1].Value;
                        var decodedTitle = System.Net.WebUtility.HtmlDecode(titleContent);
                        return $"<title>{decodedTitle}</title>";
                    },
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline
                );

                // 写入目标文件
                File.WriteAllText(destPath, htmlContent, Encoding.GetEncoding("GB2312"));
                System.Diagnostics.Debug.WriteLine($"复制并修正文件: {sourcePath} → {destPath}");
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
    private static void CopyDirectory(string sourceDir, string destDir, Dictionary<string, string>? htmlFileNameMap = null)
    {
        Directory.CreateDirectory(destDir);

        // 建立目录名映射（用于修复 HTML 中的路径）
        var dirNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var originalDirName = Path.GetFileName(dir);
            var safeDirName = SafeHhcFileName(originalDirName);
            if (!originalDirName.Equals(safeDirName, StringComparison.OrdinalIgnoreCase))
            {
                dirNameMap[originalDirName] = safeDirName;
            }
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            var extension = Path.GetExtension(file).ToLowerInvariant();

            // 对 HTML 文件特殊处理：解码 title 中的实体编码，修复内部链接
            if (extension == ".html" || extension == ".htm")
            {
                try
                {
                    // 读取 HTML 内容
                    var htmlContent = File.ReadAllText(file, Encoding.GetEncoding("GB2312"));

                    // 查找并解码 title 标签中的实体编码
                    htmlContent = System.Text.RegularExpressions.Regex.Replace(
                        htmlContent,
                        @"<title\b[^>]*>(.*?)</title>",
                        match =>
                        {
                            var titleContent = match.Groups[1].Value;
                            var decodedTitle = System.Net.WebUtility.HtmlDecode(titleContent);
                            return $"<title>{decodedTitle}</title>";
                        },
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline
                    );

                    // 修复 HTML 内部链接（文件名）
                    if (htmlFileNameMap != null && htmlFileNameMap.Count > 0)
                    {
                        foreach (var kvp in htmlFileNameMap)
                        {
                            var originalName = kvp.Key;
                            var safeFileName = kvp.Value;

                            // 替换所有出现的原始文件名
                            var pattern = $@"(href\s*=\s*[""'])([^""']*/)?" + Regex.Escape(originalName) + @"([""'])";
                            htmlContent = Regex.Replace(
                                htmlContent,
                                pattern,
                                m => $"{m.Groups[1].Value}{m.Groups[2].Value}{safeFileName}{m.Groups[3].Value}",
                                RegexOptions.IgnoreCase
                            );
                        }
                    }

                    // 修复 HTML 内部链接（目录名）
                    if (dirNameMap.Count > 0)
                    {
                        foreach (var kvp in dirNameMap)
                        {
                            var originalDirName = kvp.Key;
                            var safeDirName = kvp.Value;

                            // 替换 href 中的目录名：href="OriginalDir/file.html" → href="SafeDir/file.html"
                            htmlContent = htmlContent.Replace($"\"{originalDirName}/", $"\"{safeDirName}/");
                            htmlContent = htmlContent.Replace($"'{originalDirName}/", $"'{safeDirName}/");
                        }
                    }

                    // 写入目标文件
                    File.WriteAllText(destFile, htmlContent, Encoding.GetEncoding("GB2312"));
                }
                catch
                {
                    // 如果处理失败，直接复制
                    File.Copy(file, destFile, overwrite: true);
                }
            }
            else
            {
                // 非 HTML 文件直接复制
                File.Copy(file, destFile, overwrite: true);
            }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var originalDirName = Path.GetFileName(dir);
            var safeDirName = SafeHhcFileName(originalDirName);
            var destSubDir = Path.Combine(destDir, safeDirName);
            CopyDirectory(dir, destSubDir, htmlFileNameMap);
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

        // 先解码可能存在的 HTML 实体（避免双重编码）
        // 例如：title 中如果已经是 &amp; 就解码为 &，然后再编码为 &amp;
        text = System.Net.WebUtility.HtmlDecode(text);

        // .hhc 文件是标准 XML 格式，必须转义 & < > " 字符
        // 注意：必须先转义 &，否则会把后续转义产生的 & 再次转义
        return text.Replace("&", "&amp;")
                   .Replace("<", "&lt;")
                   .Replace(">", "&gt;")
                   .Replace("\"", "&quot;");
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
    /// 检查字符串是否包含非 ASCII 字符（如中文）
    /// </summary>
    private static bool ContainsNonAscii(string text)
    {
        return text.Any(c => c > 127);
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
    // 缓存安全化文件名的结果，避免重复计算
    private static readonly Dictionary<string, string> _safeFileNameCache = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 文件名安全化级别
    /// </summary>
    public enum SafetyLevel
    {
        /// <summary>不安全化，保持原样</summary>
        None = 0,
        /// <summary>最小安全化：只替换文件系统非法字符</summary>
        Minimal = 1,
        /// <summary>完全安全化：替换所有可能导致 CHM 问题的字符（默认）</summary>
        Full = 2
    }

    /// <summary>
    /// 当前使用的安全化级别（默认完全安全化）
    /// </summary>
    public static SafetyLevel CurrentSafetyLevel { get; set; } = SafetyLevel.None;

    /// <summary>
    /// 检查名称是否包含 CHM 不兼容的特殊字符
    /// </summary>
    /// <param name="name">要检查的名称</param>
    /// <returns>包含问题的字符描述，如果没有问题则返回 null</returns>
    public static string? CheckCHMProblematicCharacters(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var problematicChars = new List<char>();
        int dotCount = 0;
        int lastDotIndex = name.LastIndexOf('.');

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '.')
            {
                dotCount++;
                // 多个点可能有问题（除了扩展名的最后一个点）
                if (i != lastDotIndex && lastDotIndex > 0)
                {
                    if (!problematicChars.Contains('.'))
                        problematicChars.Add('.');
                }
            }
            else if (c == '&' || c == '+' || c == '#' || c == '%' || c == '!' ||
                     c == '@' || c == '$' || c == '^' || c == '*' || c == '|' ||
                     c == ';' || c == ',' || c == '\'' || c == '"' || c == '<' ||
                     c == '>' || c == '?' || c == '`' || c == '=' ||
                     c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}')
            {
                if (!problematicChars.Contains(c))
                    problematicChars.Add(c);
            }
        }

        if (problematicChars.Count == 0) return null;

        // 构建描述字符串
        var charDescriptions = problematicChars.Select(c =>
        {
            return c switch
            {
                '&' => "&",
                '+' => "+",
                '#' => "#",
                '%' => "%",
                '!' => "!",
                '@' => "@",
                '$' => "$",
                '^' => "^",
                '*' => "*",
                '|' => "|",
                ';' => ";",
                ',' => ",",
                '\'' => "'",
                '"' => "\"",
                '<' => "<",
                '>' => ">",
                '?' => "?",
                '`' => "`",
                '=' => "=",
                '(' => "(",
                ')' => ")",
                '[' => "[",
                ']' => "]",
                '{' => "{",
                '}' => "}",
                '.' => "多余的点",
                _ => c.ToString()
            };
        }).ToList();

        return string.Join("、", charDescriptions);
    }

    private static string SafeHhcFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "untitled";

        // 根据安全化级别处理
        switch (CurrentSafetyLevel)
        {
            case SafetyLevel.None:
                // 不安全化，直接返回
                return name;

            case SafetyLevel.Minimal:
                // 最小安全化：只替换文件系统非法字符
                return SanitizeFileName(name);

            case SafetyLevel.Full:
            default:
                // 完全安全化（原有逻辑）
                break;
        }

        // 检查缓存
        if (_safeFileNameCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        // 快速检查：如果文件名已经是安全的，直接返回
        int lastDotIndex = name.LastIndexOf('.');
        bool needsSanitization = false;

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '.' && i != lastDotIndex && lastDotIndex > 0)
            {
                needsSanitization = true;
                break;
            }
            if (c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}' ||
                char.IsWhiteSpace(c) ||
                c == '&' || c == '+' || c == '#' || c == '%' || c == '!' || c == '@' ||
                c == '$' || c == '^' || c == '*' || c == '|' || c == ';' || c == ',' ||
                c == '\'' || c == '"' || c == '<' || c == '>' || c == '?' || c == '`' || c == '=')
            {
                needsSanitization = true;
                break;
            }
        }

        // 如果不需要安全化，直接返回并缓存
        if (!needsSanitization)
        {
            _safeFileNameCache[name] = name;
            return name;
        }

        // 需要安全化：逐字符处理
        var sb = new StringBuilder(name.Length);
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

        // 只删除首尾的下划线，如果它们是由空白字符或特殊字符开头/结尾导致的
        // 但保留由括号等字符转换来的下划线
        // 实际上，我们不应该无条件 Trim，因为这会删除有意义的下划线
        // 例如：(test) 应该是 _test_，而不是 test

        if (string.IsNullOrEmpty(result))
        {
            result = "untitled";
        }

        // 缓存结果
        _safeFileNameCache[name] = result;
        return result;
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
        // CHM 的 .hhc 文件要求使用反斜杠 \ 作为路径分隔符
        return string.Join("\\", parts);
    }

    // ========== 重构后的统一文件复制逻辑 ==========

    /// <summary>
    /// 统一的文件复制和链接修复逻辑（重构版本）
    /// </summary>
    private static void CopyFilesToSrc_Refactored(string srcDir, IReadOnlyList<Models.DocumentNode> rootNodes)
    {
        if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir);

        // 全局映射表：原始名称 -> 安全化名称
        var globalFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var globalDirMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var processedSourceDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 第一步：遍历所有节点，复制文件并建立映射
        System.Diagnostics.Debug.WriteLine("========== 第一步：复制文件并建立映射 ==========");

        foreach (var node in rootNodes.SelectMany(r => r.GetAllFileNodes()))
        {
            string? sourcePath = null;
            string? sourceRootDir = null;

            // 确定源路径和源根目录
            if (node.NodeType == Models.NodeType.ApiHtml && !string.IsNullOrEmpty(node.SourcePath))
            {
                sourcePath = node.SourcePath;

                // 查找 API HTML 根目录
                sourceRootDir = node.ApiHtmlSourceDir;
                if (string.IsNullOrEmpty(sourceRootDir))
                {
                    var ancestor = node.Parent;
                    while (ancestor != null && string.IsNullOrEmpty(sourceRootDir))
                    {
                        sourceRootDir = ancestor.ApiHtmlSourceDir;
                        ancestor = ancestor.Parent;
                    }
                }
            }
            else if (node.NodeType == Models.NodeType.Word && !string.IsNullOrEmpty(node.ConvertedHtmlPath))
            {
                sourcePath = node.ConvertedHtmlPath;

                // Word 文件：找到文档的根目录
                var outputDir = Path.GetDirectoryName(srcDir);
                var htmlDir = Path.Combine(outputDir ?? "", "html");
                var sourceFileDir = Path.GetDirectoryName(sourcePath);

                if (sourceFileDir != null && sourceFileDir.StartsWith(htmlDir, StringComparison.OrdinalIgnoreCase))
                {
                    var relPath = sourceFileDir.Substring(htmlDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                    var firstSep = relPath.IndexOfAny(new[] { Path.DirectorySeparatorChar, '/' });
                    string docRootName = firstSep > 0 ? relPath.Substring(0, firstSep) : relPath;
                    sourceRootDir = Path.Combine(htmlDir, docRootName);
                }
            }
            else if (node.NodeType == Models.NodeType.Html && !string.IsNullOrEmpty(node.SourcePath))
            {
                sourcePath = node.SourcePath;

                // 普通 HTML 文件：向上查找最顶层的 Folder 类型父节点
                // 这个 Folder 节点就是用户选择的根文件夹
                var folderNode = node.Parent;
                Models.DocumentNode? topLevelFolder = null;

                while (folderNode != null)
                {
                    if (folderNode.NodeType == Models.NodeType.Folder)
                    {
                        topLevelFolder = folderNode;
                    }
                    folderNode = folderNode.Parent;
                }

                if (topLevelFolder != null && !string.IsNullOrEmpty(topLevelFolder.Title))
                {
                    // 从 sourcePath 中找到包含 topLevelFolder.Title 的部分
                    var folderTitle = topLevelFolder.Title;
                    var sourcePathParts = sourcePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // 找到文件夹名称在路径中的位置
                    for (int i = sourcePathParts.Length - 1; i >= 0; i--)
                    {
                        if (sourcePathParts[i].Equals(folderTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            // 找到了，重建到该文件夹的路径
                            sourceRootDir = string.Join(Path.DirectorySeparatorChar.ToString(), sourcePathParts.Take(i + 1));
                            break;
                        }
                    }
                }

                // 兜底：使用文件的父目录
                if (string.IsNullOrEmpty(sourceRootDir))
                {
                    sourceRootDir = Path.GetDirectoryName(sourcePath);
                }
            }

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                continue;
            }

            // 递归复制整个源目录树，建立映射（每个源目录只处理一次）
            if (!string.IsNullOrEmpty(sourceRootDir) && Directory.Exists(sourceRootDir) && !processedSourceDirs.Contains(sourceRootDir))
            {
                processedSourceDirs.Add(sourceRootDir);

                // 计算目标路径
                string destDir;

                if (node.NodeType == Models.NodeType.Word)
                {
                    // Word 节点：目标是 src/{安全化的目录名}/
                    var sourceRootDirName = Path.GetFileName(sourceRootDir);
                    var safeRootDirName = SafeHhcFileName(sourceRootDirName);
                    destDir = Path.Combine(srcDir, safeRootDirName);
                }
                else if (node.NodeType == Models.NodeType.ApiHtml)
                {
                    // API HTML 节点：复制根目录的内容到 src/ 下
                    // 因为 node.RelativePath 是相对于 ApiHtmlSourceDir 的，不包含顶层文件夹名
                    destDir = srcDir;
                }
                else if (node.NodeType == Models.NodeType.Html)
                {
                    // 普通 HTML 文件夹：目标是 src/{顶层文件夹名}/
                    // 因为 node.RelativePath 包含了顶层文件夹名
                    var sourceRootDirName = Path.GetFileName(sourceRootDir);
                    var safeRootDirName = SafeHhcFileName(sourceRootDirName);
                    destDir = Path.Combine(srcDir, safeRootDirName);
                }
                else
                {
                    // 其他情况：基于 node.RelativePath 计算
                    var relativePath = SafeHhcRelativePath(node.RelativePath);
                    var destPath = Path.Combine(srcDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    destDir = Path.GetDirectoryName(destPath) ?? srcDir;
                }

                if (!string.IsNullOrEmpty(destDir))
                {
                    System.Diagnostics.Debug.WriteLine($"\n复制目录树: {sourceRootDir} → {destDir}");
                    CopyDirectoryRecursive(sourceRootDir, destDir, globalFileMap, globalDirMap);
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"\n映射统计：{globalFileMap.Count} 个文件，{globalDirMap.Count} 个目录");

        // 第二步：统一修复所有 HTML 文件的链接
        System.Diagnostics.Debug.WriteLine("\n========== 第二步：修复所有 HTML 文件的链接 ==========");

        var allHtmlFiles = Directory.GetFiles(srcDir, "*.html", SearchOption.AllDirectories);
        System.Diagnostics.Debug.WriteLine($"找到 {allHtmlFiles.Length} 个 HTML 文件需要处理");

        int fixedCount = 0;
        foreach (var htmlFile in allHtmlFiles)
        {
            if (FixHtmlLinks(htmlFile, globalFileMap, globalDirMap))
            {
                fixedCount++;
            }
        }

        System.Diagnostics.Debug.WriteLine($"\n修复了 {fixedCount} 个 HTML 文件");
        System.Diagnostics.Debug.WriteLine("========== 完成 ==========");
    }

    /// <summary>
    /// 递归复制目录内容，建立映射
    /// </summary>
    private static void CopyDirectoryRecursive(
        string sourceDir,
        string destDir,
        Dictionary<string, string> fileMap,
        Dictionary<string, string> dirMap)
    {
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // 复制所有文件
        foreach (var sourceFile in Directory.GetFiles(sourceDir))
        {
            var originalFileName = Path.GetFileName(sourceFile);
            var safeFileName = SafeHhcFileName(originalFileName);
            var destFile = Path.Combine(destDir, safeFileName);

            // 记录映射关系
            if (!originalFileName.Equals(safeFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (!fileMap.ContainsKey(originalFileName))
                {
                    fileMap[originalFileName] = safeFileName;
                    System.Diagnostics.Debug.WriteLine($"  文件映射: {originalFileName} → {safeFileName}");
                }
            }

            // 复制文件
            File.Copy(sourceFile, destFile, overwrite: true);
        }

        // 递归处理子目录
        foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
        {
            var originalDirName = Path.GetFileName(sourceSubDir);
            var safeDirName = SafeHhcFileName(originalDirName);
            var destSubDir = Path.Combine(destDir, safeDirName);

            // 记录目录映射
            if (!originalDirName.Equals(safeDirName, StringComparison.OrdinalIgnoreCase))
            {
                if (!dirMap.ContainsKey(originalDirName))
                {
                    dirMap[originalDirName] = safeDirName;
                    System.Diagnostics.Debug.WriteLine($"  目录映射: {originalDirName} → {safeDirName}");
                }
            }

            // 递归复制
            CopyDirectoryRecursive(sourceSubDir, destSubDir, fileMap, dirMap);
        }
    }

    /// <summary>
    /// 修复 HTML 文件中的所有链接
    /// </summary>
    /// <returns>是否进行了修改</returns>
    private static bool FixHtmlLinks(
        string htmlFile,
        Dictionary<string, string> fileMap,
        Dictionary<string, string> dirMap)
    {
        try
        {
            var htmlContent = File.ReadAllText(htmlFile, Encoding.GetEncoding("GB2312"));
            bool modified = false;

            // 解码 title 标签中的实体编码
            htmlContent = Regex.Replace(
                htmlContent,
                @"<title\b[^>]*>(.*?)</title>",
                match =>
                {
                    var titleContent = match.Groups[1].Value;
                    var decodedTitle = System.Net.WebUtility.HtmlDecode(titleContent);
                    if (titleContent != decodedTitle)
                    {
                        modified = true;
                        return $"<title>{decodedTitle}</title>";
                    }
                    return match.Value;
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            // 修复所有 href 和 src 属性
            var linkPattern = @"((?:href|src)\s*=\s*[""'])([^""']+)([""'])";
            int replacementCount = 0;

            htmlContent = Regex.Replace(htmlContent, linkPattern, match =>
            {
                var prefix = match.Groups[1].Value;  // href=" 或 src='
                var path = match.Groups[2].Value;    // 链接路径
                var suffix = match.Groups[3].Value;  // " 或 '

                // 跳过绝对路径和特殊协议
                if (path.StartsWith("http://") || path.StartsWith("https://") ||
                    path.StartsWith("//") || path.StartsWith("#") || path.StartsWith("javascript:"))
                {
                    return match.Value;
                }

                var originalPath = path;
                var modifiedPath = path;

                // 替换路径中的每个部分
                var parts = modifiedPath.Split('/', '\\');
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (string.IsNullOrEmpty(part) || part == ".." || part == ".") continue;

                    // 最后一部分可能是文件名
                    if (i == parts.Length - 1)
                    {
                        // 尝试作为文件名替换
                        if (fileMap.TryGetValue(part, out var safeFileName))
                        {
                            parts[i] = safeFileName;
                        }
                    }
                    else
                    {
                        // 中间部分是目录名
                        if (dirMap.TryGetValue(part, out var safeDirName))
                        {
                            parts[i] = safeDirName;
                        }
                    }
                }

                modifiedPath = string.Join("/", parts);

                if (originalPath != modifiedPath)
                {
                    modified = true;
                    replacementCount++;
                    System.Diagnostics.Debug.WriteLine($"    [{Path.GetFileName(htmlFile)}] {originalPath} → {modifiedPath}");
                }

                return $"{prefix}{modifiedPath}{suffix}";
            }, RegexOptions.IgnoreCase);

            // 如果有修改，写回文件
            if (modified)
            {
                File.WriteAllText(htmlFile, htmlContent, Encoding.GetEncoding("GB2312"));
                System.Diagnostics.Debug.WriteLine($"  修复了 {Path.GetFileName(htmlFile)}: {replacementCount} 个链接");
            }

            return modified;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"  处理 {htmlFile} 失败: {ex.Message}");
            return false;
        }
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
