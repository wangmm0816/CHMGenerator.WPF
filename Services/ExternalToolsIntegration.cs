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
    /// </summary>
    /// <param name="pythonToolsPath">Python 工具目录路径（包含 DocToCHM.py）</param>
    /// <param name="docxPath">Word 文档路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <param name="progress">进度报告</param>
    /// <returns>转换后的 HTML 文件路径，失败返回空字符串</returns>
    public static async Task<string> ConvertWordToPythonHtml(
        string pythonToolsPath,
        string docxPath,
        string outputDir,
        IProgress<string>? progress = null)
    {
        if (!File.Exists(docxPath))
        {
            progress?.Report($"Word 文件不存在: {docxPath}");
            return "";
        }

        var docToChmScript = Path.Combine(pythonToolsPath, "DocToCHM.py");
        if (!File.Exists(docToChmScript))
        {
            progress?.Report($"未找到 Python 脚本: {docToChmScript}");
            return "";
        }

        try
        {
            Directory.CreateDirectory(outputDir);

            // 生成唯一的输出子目录名
            var outputSubDir = Path.GetFileNameWithoutExtension(docxPath);
            outputSubDir = SanitizeFileName(outputSubDir);

            // 调用 Python: python DocToCHM.py <docx> <out_html_dir> -o <output_root>
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{docToChmScript}\" \"{docxPath}\" \"{outputSubDir}\" -o \"{outputDir}\"",
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
                // 查找生成的 HTML 文件
                // Python 会在 outputDir/outputSubDir 下生成文件
                var targetDir = Path.Combine(outputDir, outputSubDir);
                if (Directory.Exists(targetDir))
                {
                    var htmlFiles = Directory.GetFiles(targetDir, "*.html", SearchOption.TopDirectoryOnly);
                    if (htmlFiles.Length > 0)
                    {
                        progress?.Report($"Python 转换成功: {htmlFiles[0]}");
                        return htmlFiles[0];
                    }
                }

                // 也可能直接在 outputDir 下
                var htmlFiles2 = Directory.GetFiles(outputDir, "*.html", SearchOption.AllDirectories);
                if (htmlFiles2.Length > 0)
                {
                    progress?.Report($"Python 转换成功: {htmlFiles2[0]}");
                    return htmlFiles2[0];
                }

                progress?.Report("Python 转换完成，但未找到生成的 HTML 文件");
                return "";
            }
            else
            {
                progress?.Report($"Python 转换失败，退出码: {process.ExitCode}");
                if (errorBuilder.Length > 0)
                {
                    progress?.Report($"错误输出: {errorBuilder}");
                }
                return "";
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"调用 Python 脚本失败: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 批量转换多个 Word 文档（可选：使用 DocToHtmlByDir.py 或循环调用 DocToCHM.py）
    /// </summary>
    public static async Task<Dictionary<string, string>> ConvertMultipleWords(
        string pythonToolsPath,
        List<string> docxPaths,
        string outputDir,
        IProgress<string>? progress = null)
    {
        var results = new Dictionary<string, string>();

        for (int i = 0; i < docxPaths.Count; i++)
        {
            var docxPath = docxPaths[i];
            progress?.Report($"转换 {i + 1}/{docxPaths.Count}: {Path.GetFileName(docxPath)}");

            var subOutputDir = Path.Combine(outputDir, $"doc_{i + 1}");
            var htmlPath = await ConvertWordToPythonHtml(pythonToolsPath, docxPath, subOutputDir, progress);

            results[docxPath] = htmlPath;
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
}
