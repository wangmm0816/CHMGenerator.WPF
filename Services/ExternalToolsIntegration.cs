using System.Diagnostics;
using System.IO;
using System.Text;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 外部工具集成服务
/// 支持 Python doc2html 转换 Word 文档
/// </summary>
public class ExternalToolsIntegration
{
    /// <summary>
    /// 使用 Python DocToCHM.py 转换单个 Word 文档为 HTML
    /// DocToCHM.py 输出结构：
    /// - output/html/{out_html_dir}/*.html (HTML文件)
    /// - output/html/{out_html_dir}/images/*.png (图片)
    /// - output/html/{out_html_dir}.txt (目录结构配置)
    /// - output/html/css/*, scripts/*, resource/* (公共资源)
    /// </summary>
    /// <param name="pythonToolsPath">Python 工具目录路径（包含 DocToCHM.py）</param>
    /// <param name="docxPath">Word 文档路径</param>
    /// <param name="outputDir">输出根目录（对应 -o 参数）</param>
    /// <param name="title">文档标题（对应 -t 参数）</param>
    /// <param name="progress">进度报告</param>
    /// <returns>包含生成的HTML目录和txt配置文件的结果对象</returns>
    public static async Task<PythonConversionResult> ConvertWordToPythonHtml(
        string pythonToolsPath,
        string docxPath,
        string outputDir,
        string title,
        IProgress<string>? progress = null)
    {
        var result = new PythonConversionResult();

        if (!File.Exists(docxPath))
        {
            result.ErrorMessage = $"Word 文件不存在: {docxPath}";
            progress?.Report(result.ErrorMessage);
            return result;
        }

        var docToChmScript = Path.Combine(pythonToolsPath, "DocToCHM.py");
        if (!File.Exists(docToChmScript))
        {
            result.ErrorMessage = $"未找到 Python 脚本: {docToChmScript}";
            progress?.Report(result.ErrorMessage);
            return result;
        }

        try
        {
            Directory.CreateDirectory(outputDir);

            // 生成唯一的输出子目录名（不包含非法字符）
            var outputSubDirName = SanitizeFileName(Path.GetFileNameWithoutExtension(docxPath));

            // Python DocToCHM.py 参数：
            // python DocToCHM.py <input_file> <out_html_dir> -o <output_dir> -t <title>
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{docToChmScript}\" \"{docxPath}\" \"{outputSubDirName}\" -o \"{outputDir}\" -t \"{title}\"",
                WorkingDirectory = pythonToolsPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    progress?.Report($"[Python] {e.Data}");
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    progress?.Report($"[Python Error] {e.Data}");
                }
            };

            progress?.Report($"开始调用 Python 转换: {Path.GetFileName(docxPath)}");
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Python 工具会在 outputDir 下创建 html 子目录
                // 我们传入的 outputDir 已经是项目的 html 目录了
                // 所以实际路径可能是：outputDir/html/{outputSubDirName}/ (多了一层)
                // 我们希望是：outputDir/{outputSubDirName}/

                // 先尝试直接在 outputDir 下查找
                var htmlDir = Path.Combine(outputDir, outputSubDirName);
                var txtFile = Path.Combine(outputDir, $"{outputSubDirName}.txt");

                // 如果不存在，检查是否 Python 创建了额外的 html 层级
                if (!Directory.Exists(htmlDir))
                {
                    var htmlSubDir = Path.Combine(outputDir, "html");
                    if (Directory.Exists(htmlSubDir))
                    {
                        htmlDir = Path.Combine(htmlSubDir, outputSubDirName);
                        txtFile = Path.Combine(htmlSubDir, $"{outputSubDirName}.txt");
                    }
                }

                if (Directory.Exists(htmlDir))
                {
                    // 复制 Python 工具目录中的共享资源文件（css, scripts, images, fonts）
                    // 这些文件通常在 Python 工具目录的 template 或 resources 子目录中
                    CopySharedResourcesFromPythonTools(pythonToolsPath, Path.GetDirectoryName(htmlDir)!, progress);

                    result.Success = true;
                    result.HtmlDirectory = htmlDir;
                    result.TxtConfigFile = File.Exists(txtFile) ? txtFile : "";
                    result.OutputSubDirName = outputSubDirName;

                    var htmlFiles = Directory.GetFiles(htmlDir, "*.html", SearchOption.AllDirectories);
                    progress?.Report($"Python 转换成功: 生成 {htmlFiles.Length} 个 HTML 文件");
                    progress?.Report($"  HTML 目录: {htmlDir}");
                    if (File.Exists(txtFile))
                    {
                        progress?.Report($"  配置文件: {txtFile}");
                    }
                }
                else
                {
                    result.ErrorMessage = $"未找到生成的 HTML 目录: {htmlDir}";
                    progress?.Report(result.ErrorMessage);
                }
            }
            else
            {
                result.ErrorMessage = $"Python 转换失败，退出码: {process.ExitCode}";
                if (errorBuilder.Length > 0)
                {
                    result.ErrorMessage += $"\n错误输出: {errorBuilder}";
                }
                progress?.Report(result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"调用 Python 脚本失败: {ex.Message}";
            progress?.Report(result.ErrorMessage);
            return result;
        }
    }

    /// <summary>
    /// Python 转换结果
    /// </summary>
    public class PythonConversionResult
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>生成的 HTML 文件所在目录</summary>
        public string HtmlDirectory { get; set; } = "";

        /// <summary>生成的 txt 配置文件路径（包含目录结构）</summary>
        public string TxtConfigFile { get; set; } = "";

        /// <summary>输出子目录名称</summary>
        public string OutputSubDirName { get; set; } = "";

        /// <summary>错误消息</summary>
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// 批量转换多个 Word 文档
    /// </summary>
    public static async Task<List<PythonConversionResult>> ConvertMultipleWords(
        string pythonToolsPath,
        List<string> docxPaths,
        string outputDir,
        IProgress<string>? progress = null)
    {
        var results = new List<PythonConversionResult>();

        for (int i = 0; i < docxPaths.Count; i++)
        {
            var docxPath = docxPaths[i];
            var title = Path.GetFileNameWithoutExtension(docxPath);
            progress?.Report($"转换 {i + 1}/{docxPaths.Count}: {Path.GetFileName(docxPath)}");

            var result = await ConvertWordToPythonHtml(pythonToolsPath, docxPath, outputDir, title, progress);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// 安全化文件名（移除特殊字符）
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars));
        return sanitized;
    }

    /// <summary>
    /// 从 Python 工具目录复制共享资源文件（css, scripts, images, fonts）到输出目录
    /// </summary>
    private static void CopySharedResourcesFromPythonTools(string pythonToolsPath, string targetDir, IProgress<string>? progress = null)
    {
        try
        {
            // Python 工具的共享资源可能在以下位置：
            // 1. pythonToolsPath/resource/
            // 2. pythonToolsPath/resources/
            // 3. pythonToolsPath/template/
            // 4. pythonToolsPath/ 本身

            var sharedDirNames = new[] { "css", "scripts", "images", "fonts" };
            var searchPaths = new[]
            {
                Path.Combine(pythonToolsPath, "resource"),
                Path.Combine(pythonToolsPath, "resources"),
                Path.Combine(pythonToolsPath, "template"),
                pythonToolsPath
            };

            foreach (var sharedDirName in sharedDirNames)
            {
                bool copied = false;
                foreach (var searchPath in searchPaths)
                {
                    var sourceDir = Path.Combine(searchPath, sharedDirName);
                    if (Directory.Exists(sourceDir))
                    {
                        var destDir = Path.Combine(targetDir, sharedDirName);
                        CopyDirectory(sourceDir, destDir);
                        progress?.Report($"  复制共享资源: {sharedDirName} → {destDir}");
                        copied = true;
                        break;
                    }
                }

                if (!copied)
                {
                    // 没找到该共享资源目录，记录日志但不报错
                    System.Diagnostics.Debug.WriteLine($"未找到共享资源目录: {sharedDirName}");
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"  警告: 复制共享资源失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"复制共享资源失败: {ex.Message}");
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

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
