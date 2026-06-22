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
    public GeneratedProject Generate(string outputDir, string srcDir, string title,
        string defaultTopic, IReadOnlyList<Models.DocumentNode> rootNodes,
        bool fullTextSearch = true, bool binaryToc = true, bool autoIndex = true)
    {
        // 把所有文件复制到 src/ 下，按 RelativePath 摆放
        CopyFilesToSrc(srcDir, rootNodes);

        var hhpPath = Path.Combine(outputDir, "project.hhp");
        var hhcPath = Path.Combine(outputDir, "toc.hhc");
        var hhkPath = Path.Combine(outputDir, "index.hhk");

        // 收集所有文件节点
        var allFiles = rootNodes.SelectMany(r => r.GetAllFileNodes()).ToList();

        GenerateHhp(hhpPath, srcDir, title, defaultTopic, allFiles, fullTextSearch, binaryToc, autoIndex);
        GenerateHhc(hhcPath, srcDir, title, defaultTopic, rootNodes, binaryToc);
        GenerateHhk(hhkPath, srcDir, allFiles);

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

    private void CopyFilesToSrc(string srcDir, IReadOnlyList<Models.DocumentNode> rootNodes)
    {
        if (!Directory.Exists(srcDir)) Directory.CreateDirectory(srcDir);

        foreach (var node in rootNodes.SelectMany(r => r.GetAllFileNodes()))
        {
            if (string.IsNullOrEmpty(node.EffectiveHtmlPath) || !File.Exists(node.EffectiveHtmlPath))
                continue;

            // 计算目标路径（相对 src 的 RelativePath）
            var relativePath = node.RelativePath;
            var destPath = Path.Combine(srcDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // 复制 HTML 文件
            File.Copy(node.EffectiveHtmlPath, destPath, overwrite: true);

            // 复制关联的图片目录（Word 转换产生的）
            if (node.NodeType == Models.NodeType.Word)
            {
                var imageDir = Path.Combine(
                    Path.GetDirectoryName(node.ConvertedHtmlPath) ?? "",
                    Path.GetFileNameWithoutExtension(node.ConvertedHtmlPath) + "_images");

                if (Directory.Exists(imageDir))
                {
                    var destImageDir = Path.Combine(destDir ?? srcDir,
                        Path.GetFileNameWithoutExtension(destPath) + "_images");
                    if (!Directory.Exists(destImageDir)) Directory.CreateDirectory(destImageDir);
                    foreach (var img in Directory.GetFiles(imageDir))
                    {
                        File.Copy(img, Path.Combine(destImageDir, Path.GetFileName(img)), overwrite: true);
                    }
                }
            }
        }
    }

    private void GenerateHhp(string hhpPath, string srcDir, string title, string defaultTopic,
        List<Models.DocumentNode> allFiles, bool fullTextSearch, bool binaryToc, bool autoIndex)
    {
        var sb = new StringBuilder();

        sb.AppendLine("[OPTIONS]");
        sb.AppendLine("Compatibility=1.1 or later");
        sb.AppendLine($"Compiled file={SanitizeFileName(title)}.chm");
        sb.AppendLine("Contents file=toc.hhc");
        sb.AppendLine("Index file=index.hhk");
        sb.AppendLine("Default Window=Main");
        sb.AppendLine($"Default topic={defaultTopic}");
        sb.AppendLine("Display compile progress=Yes");
        sb.AppendLine($"Full-text search={(fullTextSearch ? "Yes" : "No")}");
        sb.AppendLine("Language=0x804");
        sb.AppendLine($"Title={title}");
        sb.AppendLine($"Binary TOC={(binaryToc ? "Yes" : "No")}");
        sb.AppendLine($"Auto Index={(autoIndex ? "Yes" : "No")}");
        sb.AppendLine("Enhance Decompilation=No");
        sb.AppendLine();
        sb.AppendLine("[WINDOWS]");
        sb.AppendLine($"Main=\"{title}\",\"toc.hhc\",\"index.hhk\",\"{defaultTopic}\",\"{defaultTopic}\",,,,,0x63520,,0x387e,,,,,,0");
        sb.AppendLine();
        sb.AppendLine("[FILES]");

        foreach (var file in allFiles)
        {
            sb.AppendLine(file.RelativePath);
        }

        File.WriteAllText(hhpPath, sb.ToString(), Encoding.GetEncoding("GB2312"));
    }

    private void GenerateHhc(string hhcPath, string srcDir, string title, string defaultTopic,
        IReadOnlyList<Models.DocumentNode> rootNodes, bool binaryToc)
    {
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

        // 首页节点（指向默认主题）
        if (!string.IsNullOrEmpty(defaultTopic))
        {
            sb.AppendLine("<ul>");
            sb.AppendLine("    <li><object type=\"text/sitemap\">");
            sb.AppendLine($"        <param name=\"Name\" value=\"{EscapeXml(title)}\">");
            sb.AppendLine($"        <param name=\"Local\" value=\"{defaultTopic}\">");
            sb.AppendLine("    </object>");
        }

        // 递归构建树
        sb.AppendLine("    <ul>");
        foreach (var node in rootNodes)
        {
            BuildHhcNode(sb, node, 2);
        }
        sb.AppendLine("    </ul>");

        if (!string.IsNullOrEmpty(defaultTopic))
        {
            sb.AppendLine("    </li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(hhcPath, sb.ToString(), Encoding.GetEncoding("GB2312"));
    }

    private void BuildHhcNode(StringBuilder sb, Models.DocumentNode node, int level)
    {
        string indent = new string(' ', level * 4);

        if (node.IsFolder)
        {
            // 文件夹节点
            sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
            sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
            sb.AppendLine($"{indent}</object>");
            sb.AppendLine($"{indent}<ul>");
            foreach (var child in node.Children)
            {
                BuildHhcNode(sb, child, level + 1);
            }
            sb.AppendLine($"{indent}</ul>");
            sb.AppendLine($"{indent}</li>");
        }
        else
        {
            // 文件节点
            sb.AppendLine($"{indent}<li><object type=\"text/sitemap\">");
            sb.AppendLine($"{indent}    <param name=\"Name\" value=\"{EscapeXml(node.Title)}\">");
            sb.AppendLine($"{indent}    <param name=\"Local\" value=\"{node.RelativePath}\">");
            sb.AppendLine($"{indent}</object>");
            sb.AppendLine($"{indent}</li>");
        }
    }

    private void GenerateHhk(string hhkPath, string srcDir, List<Models.DocumentNode> allFiles)
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

        foreach (var file in allFiles.OrderBy(f => f.Title, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"<li><object type=\"text/sitemap\">");
            sb.AppendLine($"  <param name=\"Name\" value=\"{EscapeXml(file.Title)}\">");
            sb.AppendLine($"  <param name=\"Local\" value=\"{file.RelativePath}\">");
            sb.AppendLine($"</object></li>");
        }

        sb.AppendLine("</ul>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(hhkPath, sb.ToString(), Encoding.GetEncoding("GB2312"));
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
                WorkingDirectory = project.OutputDir,
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
