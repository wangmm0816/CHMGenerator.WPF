using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// API HTML 目录扫描器
/// 扫描 API HTML 目录，解析文件的 title 标签，建立父子关系
/// </summary>
public class ApiHtmlScanner
{
    /// <summary>
    /// 扫描 API HTML 目录，返回根节点列表
    /// </summary>
    /// <param name="apiHtmlDir">API HTML 目录路径</param>
    /// <returns>根节点列表（已建立好父子关系）</returns>
    public static List<DocumentNode> Scan(string apiHtmlDir)
    {
        if (!Directory.Exists(apiHtmlDir))
            return new List<DocumentNode>();

        // 1. 收集所有 HTML 文件（忽略 css/scripts）
        var allHtmlFiles = CollectHtmlFiles(apiHtmlDir);
        if (allHtmlFiles.Count == 0)
            return new List<DocumentNode>();

        // 2. 读取每个 HTML 的 title
        var fileTitleMap = new Dictionary<string, string>();
        foreach (var htmlFile in allHtmlFiles)
        {
            var title = ExtractHtmlTitle(htmlFile);
            fileTitleMap[htmlFile] = title ?? Path.GetFileNameWithoutExtension(htmlFile);
        }

        // 3. 建立文件夹到入口文件的映射
        var folderEntryMap = BuildFolderEntryMap(apiHtmlDir, allHtmlFiles, fileTitleMap);

        // 4. 为每个 HTML 确定父级
        var htmlInfoList = new List<HtmlFileInfo>();
        foreach (var htmlFile in allHtmlFiles)
        {
            var info = new HtmlFileInfo
            {
                FullPath = htmlFile,
                RelativePath = GetRelativePath(htmlFile, apiHtmlDir),
                Title = fileTitleMap[htmlFile],
                ParentPath = FindParentEntry(htmlFile, apiHtmlDir, folderEntryMap)
            };
            htmlInfoList.Add(info);
        }

        // 5. 构建节点树
        var rootNodes = BuildNodeTree(htmlInfoList, apiHtmlDir);

        // 6. 在所有根节点上保存 API HTML 源目录信息
        foreach (var rootNode in rootNodes)
        {
            rootNode.ApiHtmlSourceDir = apiHtmlDir;
        }

        return rootNodes;
    }

    /// <summary>
    /// 检测文件夹是否是 API HTML 目录
    /// </summary>
    public static bool IsApiHtmlDirectory(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return false;

        var hasCss = Directory.Exists(Path.Combine(folderPath, "css"));
        var hasScripts = Directory.Exists(Path.Combine(folderPath, "scripts"));
        var htmlCount = Directory.GetFiles(folderPath, "*.html", SearchOption.AllDirectories).Length;

        // 如果有 css 或 scripts 文件夹，且 HTML 文件数量 > 5，判定为 API HTML 目录
        return (hasCss || hasScripts) && htmlCount > 5;
    }

    private class HtmlFileInfo
    {
        public string FullPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string ParentPath { get; set; } = "";
    }

    /// <summary>
    /// 递归收集所有 HTML 文件，忽略 css/scripts 文件夹
    /// </summary>
    private static List<string> CollectHtmlFiles(string directory)
    {
        var result = new List<string>();

        // 忽略的文件夹名称
        var ignoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "css", "scripts", "images", "fonts"
        };

        try
        {
            // 收集当前目录的 HTML 文件
            var htmlFiles = Directory.GetFiles(directory, "*.html");
            result.AddRange(htmlFiles);

            // 递归处理子文件夹
            var subDirs = Directory.GetDirectories(directory);
            foreach (var subDir in subDirs)
            {
                var folderName = Path.GetFileName(subDir);
                if (!ignoredFolders.Contains(folderName))
                {
                    result.AddRange(CollectHtmlFiles(subDir));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"扫描 HTML 文件时出错: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 提取 HTML 文件的 title 标签内容
    /// </summary>
    private static string? ExtractHtmlTitle(string htmlFile)
    {
        try
        {
            // 尝试多种编码读取 HTML 文件
            Encoding[] encodings = { Encoding.UTF8, Encoding.GetEncoding("GB2312"), Encoding.Default };

            foreach (var encoding in encodings)
            {
                try
                {
                    var content = File.ReadAllText(htmlFile, encoding);

                    // 检查 HTML 中是否声明了编码
                    var charsetMatch = Regex.Match(content, @"charset\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
                    if (charsetMatch.Success)
                    {
                        var declaredEncoding = charsetMatch.Groups[1].Value;
                        if (!declaredEncoding.Equals(encoding.WebName, StringComparison.OrdinalIgnoreCase))
                        {
                            // 使用声明的编码重新读取
                            try
                            {
                                var correctEncoding = Encoding.GetEncoding(declaredEncoding);
                                content = File.ReadAllText(htmlFile, correctEncoding);
                            }
                            catch
                            {
                                // 如果无法获取声明的编码，使用当前编码
                            }
                        }
                    }

                    var match = Regex.Match(content, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success)
                    {
                        var title = match.Groups[1].Value.Trim();
                        // 检查是否有乱码（包含大量问号或方框字符）
                        if (!string.IsNullOrEmpty(title) && !title.Contains("�") && title.Length > 0)
                        {
                            return title;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取 HTML title 失败 ({htmlFile}): {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 建立文件夹到入口文件的映射
    /// 规则：如果 HTML 的 title 与子文件夹名称相同，则该 HTML 是子文件夹的入口文件
    /// </summary>
    private static Dictionary<string, string> BuildFolderEntryMap(
        string rootDir, List<string> allHtmlFiles, Dictionary<string, string> fileTitleMap)
    {
        var folderEntryMap = new Dictionary<string, string>();

        // 递归处理每个文件夹
        BuildFolderEntryMapRecursive(rootDir, allHtmlFiles, fileTitleMap, folderEntryMap);

        return folderEntryMap;
    }

    private static void BuildFolderEntryMapRecursive(
        string directory, List<string> allHtmlFiles, Dictionary<string, string> fileTitleMap,
        Dictionary<string, string> folderEntryMap)
    {
        try
        {
            // 获取当前文件夹的子文件夹
            var subFolders = Directory.GetDirectories(directory)
                .Where(d => !IsIgnoredFolder(Path.GetFileName(d)))
                .ToList();

            // 获取当前文件夹的 HTML 文件
            var htmlFilesInDir = allHtmlFiles
                .Where(f => Path.GetDirectoryName(f)?.Equals(directory, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // 检查每个 HTML 文件是否是某个子文件夹的入口
            foreach (var htmlFile in htmlFilesInDir)
            {
                var title = fileTitleMap.ContainsKey(htmlFile) ? fileTitleMap[htmlFile] : "";

                foreach (var subFolder in subFolders)
                {
                    var folderName = Path.GetFileName(subFolder);

                    // 如果 title 与子文件夹名称相同，则该 HTML 是入口文件
                    if (title.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        folderEntryMap[subFolder] = htmlFile;
                        break;
                    }
                }
            }

            // 递归处理子文件夹
            foreach (var subFolder in subFolders)
            {
                BuildFolderEntryMapRecursive(subFolder, allHtmlFiles, fileTitleMap, folderEntryMap);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"构建入口映射时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 查找 HTML 文件的父级入口文件
    /// </summary>
    private static string FindParentEntry(string htmlFile, string rootDir, Dictionary<string, string> folderEntryMap)
    {
        var fileDir = Path.GetDirectoryName(htmlFile);
        if (string.IsNullOrEmpty(fileDir))
            return "";

        // 如果当前文件夹有入口文件，且当前文件不是入口文件本身
        if (folderEntryMap.TryGetValue(fileDir, out var entryFile))
        {
            if (!htmlFile.Equals(entryFile, StringComparison.OrdinalIgnoreCase))
            {
                return GetRelativePath(entryFile, rootDir);
            }
        }

        // 检查父目录下是否有与当前目录同名的 HTML 文件
        // 例如：UserCode/TheInst.html 的父级可能是 UserCode.html
        var parentDir = Path.GetDirectoryName(fileDir);
        if (!string.IsNullOrEmpty(parentDir) && parentDir.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(fileDir);
            var potentialParentFile = Path.Combine(parentDir, folderName + ".html");

            // 如果父目录下存在同名的 HTML 文件，它就是父级入口
            if (File.Exists(potentialParentFile))
            {
                return GetRelativePath(potentialParentFile, rootDir);
            }
        }

        // 向上查找父级文件夹的入口文件
        var currentParentDir = Path.GetDirectoryName(fileDir);
        while (!string.IsNullOrEmpty(currentParentDir) && currentParentDir.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
        {
            if (folderEntryMap.TryGetValue(currentParentDir, out var parentEntry))
            {
                return GetRelativePath(parentEntry, rootDir);
            }
            currentParentDir = Path.GetDirectoryName(currentParentDir);
        }

        return "";
    }

    /// <summary>
    /// 获取相对路径
    /// </summary>
    private static string GetRelativePath(string fullPath, string basePath)
    {
        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }
        return Path.GetFileName(fullPath);
    }

    /// <summary>
    /// 构建节点树
    /// </summary>
    private static List<DocumentNode> BuildNodeTree(List<HtmlFileInfo> htmlInfoList, string apiHtmlDir)
    {
        var nodeMap = new Dictionary<string, DocumentNode>();
        var rootNodes = new List<DocumentNode>();

        // 第一遍：创建所有节点
        foreach (var info in htmlInfoList)
        {
            var node = new DocumentNode
            {
                Title = info.Title,
                SourcePath = info.FullPath,
                NodeType = NodeType.ApiHtml
            };
            nodeMap[info.RelativePath] = node;
        }

        // 第二遍：建立父子关系
        foreach (var info in htmlInfoList)
        {
            var node = nodeMap[info.RelativePath];

            if (string.IsNullOrEmpty(info.ParentPath))
            {
                // 根节点
                rootNodes.Add(node);
            }
            else if (nodeMap.TryGetValue(info.ParentPath, out var parentNode))
            {
                // 设置父子关系
                node.Parent = parentNode;
                parentNode.Children.Add(node);
            }
            else
            {
                // 找不到父节点，作为根节点
                rootNodes.Add(node);
            }
        }

        return rootNodes;
    }

    private static bool IsIgnoredFolder(string folderName)
    {
        var ignoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "css", "scripts", "images", "fonts"
        };
        return ignoredFolders.Contains(folderName);
    }
}
