using System.IO;
using System.Text;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// txt 配置文件解析器
/// 支持两种格式：
/// 1. Python doc2html 格式：相对路径\t标题\t父级相对路径
/// 2. CHMGenerator 格式：src/相对路径\t标题\t父级相对路径
/// </summary>
public class TxtConfigParser
{
    /// <summary>
    /// 配置文件条目
    /// </summary>
    public class ConfigEntry
    {
        /// <summary>HTML文件相对路径（基于src目录）</summary>
        public string RelativePath { get; set; } = "";

        /// <summary>文件标题</summary>
        public string Title { get; set; } = "";

        /// <summary>父级HTML文件路径（基于src目录）</summary>
        public string ParentPath { get; set; } = "";

        /// <summary>原始相对路径（未加 src/ 前缀）</summary>
        public string OriginalRelativePath { get; set; } = "";
    }

    /// <summary>
    /// 解析配置文件
    /// </summary>
    /// <param name="txtFilePath">txt 配置文件路径</param>
    /// <param name="baseFolder">基础文件夹名称（如 "apihtml"）</param>
    /// <param name="addPrefix">是否添加 src/ 前缀（默认 true，用于兼容旧格式）</param>
    /// <returns>配置条目列表</returns>
    public static List<ConfigEntry> Parse(string txtFilePath, string? baseFolder = null, bool addPrefix = true)
    {
        var entries = new List<ConfigEntry>();

        if (!File.Exists(txtFilePath))
        {
            return entries;
        }

        try
        {
            var lines = File.ReadAllLines(txtFilePath, Encoding.UTF8);

            // 自动检测基础文件夹名称（从文件名推断）
            if (string.IsNullOrEmpty(baseFolder))
            {
                baseFolder = Path.GetFileNameWithoutExtension(txtFilePath);
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length < 2)
                    continue;

                var originalPath = parts[0].Trim();
                var title = parts[1].Trim();
                var parentPath = parts.Length >= 3 ? parts[2].Trim() : "";

                // 修正路径：如果是 chapter_N.html（没有子目录），补充为 chapter_N/chapter_N.html
                if (!originalPath.Contains('/') && !originalPath.Contains('\\') &&
                    originalPath.StartsWith("chapter_") && originalPath.EndsWith(".html"))
                {
                    var chapterName = Path.GetFileNameWithoutExtension(originalPath);
                    originalPath = $"{chapterName}/{originalPath}";
                }

                // 规范化路径：确保所有路径都以 src/ 开头（如果 addPrefix 为 true）
                var relativePath = addPrefix ? NormalizePath(originalPath, baseFolder) : originalPath.Replace('\\', '/');
                var normalizedParentPath = string.IsNullOrEmpty(parentPath)
                    ? ""
                    : (addPrefix ? NormalizePath(parentPath, baseFolder) : parentPath.Replace('\\', '/'));

                entries.Add(new ConfigEntry
                {
                    RelativePath = relativePath,
                    Title = title,
                    ParentPath = normalizedParentPath,
                    OriginalRelativePath = originalPath
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"解析配置文件失败: {txtFilePath}, 错误: {ex.Message}");
        }

        return entries;
    }

    /// <summary>
    /// 规范化路径：确保路径格式统一
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <param name="baseFolder">基础文件夹名称</param>
    /// <returns>规范化后的路径</returns>
    private static string NormalizePath(string path, string? baseFolder)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        // 统一使用正斜杠
        path = path.Replace('\\', '/');

        // 如果已经以 src/ 开头，直接返回
        if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
            return path;

        // 特殊处理：如果路径是 chapter_N.html（没有子目录），补充为 chapter_N/chapter_N.html
        if (!path.Contains('/') && path.StartsWith("chapter_") && path.EndsWith(".html"))
        {
            var chapterName = Path.GetFileNameWithoutExtension(path); // 例如 "chapter_1"
            path = $"{chapterName}/{path}"; // 变成 "chapter_1/chapter_1.html"
        }

        // 如果以基础文件夹名称开头，加上 src/ 前缀
        if (!string.IsNullOrEmpty(baseFolder) &&
            path.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
        {
            return "src/" + path;
        }

        // 否则加上 src/{baseFolder}/ 前缀
        if (!string.IsNullOrEmpty(baseFolder))
        {
            return $"src/{baseFolder}/" + path;
        }

        // 默认只加 src/ 前缀
        return "src/" + path;
    }

    /// <summary>
    /// 解析多个配置文件并合并
    /// </summary>
    /// <param name="txtFiles">配置文件路径列表</param>
    /// <returns>合并后的配置条目列表</returns>
    public static List<ConfigEntry> ParseMultiple(params string[] txtFiles)
    {
        var allEntries = new List<ConfigEntry>();

        foreach (var txtFile in txtFiles)
        {
            if (File.Exists(txtFile))
            {
                var baseFolder = Path.GetFileNameWithoutExtension(txtFile);
                var entries = Parse(txtFile, baseFolder);
                allEntries.AddRange(entries);
            }
        }

        return allEntries;
    }

    /// <summary>
    /// 从目录中查找所有 txt 配置文件并解析
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <returns>合并后的配置条目列表</returns>
    public static List<ConfigEntry> ParseFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return new List<ConfigEntry>();

        var txtFiles = Directory.GetFiles(directory, "*.txt");
        return ParseMultiple(txtFiles);
    }
}
