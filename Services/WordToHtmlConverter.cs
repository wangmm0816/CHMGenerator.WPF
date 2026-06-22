using System.Diagnostics;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Codeuctivity.OpenXmlPowerTools;
using Codeuctivity.OpenXmlPowerTools.OpenXMLWordprocessingMLToHtmlConverter;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// Word 文件转 HTML 服务
/// 使用 OpenXmlPowerTools (WmlToHtmlConverter) 实现高保真 DOCX → HTML 转换
/// </summary>
public class WordToHtmlConverter
{
    /// <summary>
    /// 将 .docx 文件转换为 HTML 文件
    /// </summary>
    public ConversionResult ConvertToHtml(string docxPath, string outputDir, string? baseName = null)
    {
        if (!File.Exists(docxPath))
            throw new FileNotFoundException($"Word 文件不存在: {docxPath}");

        var name = baseName ?? Path.GetFileNameWithoutExtension(docxPath);
        var htmlFileName = $"{SanitizeFileName(name)}.html";
        var htmlPath = Path.Combine(outputDir, htmlFileName);

        Directory.CreateDirectory(outputDir);

        string title = name;
        string htmlContent;

        // OpenXmlPowerTools 要求可写访问（它会修改图片部件），所以先复制到临时文件
        var tempDocx = Path.Combine(Path.GetTempPath(), $"chmgen_{Guid.NewGuid():N}.docx");
        File.Copy(docxPath, tempDocx, overwrite: true);

        try
        {
            using (var fs = new FileStream(tempDocx, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var wordDoc = WordprocessingDocument.Open(fs, true))
            {
                // 提取标题
                var coreProps = wordDoc.PackageProperties;
                if (!string.IsNullOrEmpty(coreProps?.Title))
                {
                    title = coreProps.Title!;
                }

                // 简单配置：图片默认内嵌为 base64 data URI，避免路径问题
                // 想要图片落盘可以传入 ImageHandler（Func<ImageInfo, XElement>）
                var settings = new WmlToHtmlConverterSettings(title);

                var htmlElement = WmlToHtmlConverter.ConvertToHtml(wordDoc, settings);
                htmlContent = htmlElement.ToString();
            }
        }
        finally
        {
            try { if (File.Exists(tempDocx)) File.Delete(tempDocx); } catch { }
        }

        // 注入 GB2312 meta（hhc.exe 老软件对 UTF-8 支持差）
        // 注意：base64 内嵌的图片在 GB2312 编码下没问题，但 hhc.exe 对超大单文件不友好
        // 如果发现编译失败，可以后续改成图片落盘方案
        htmlContent = EnsureMetaCharset(htmlContent, "gb2312");

        File.WriteAllText(htmlPath, htmlContent, Encoding.GetEncoding("GB2312"));

        return new ConversionResult
        {
            HtmlPath = htmlPath,
            Title = title,
            ImageDirectory = ""
        };
    }

    /// <summary>
    /// 批量转换
    /// </summary>
    public List<ConversionResult> ConvertBatch(IEnumerable<string> docxPaths, string outputDir,
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = docxPaths.ToList();
        var results = new List<ConversionResult>(list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((i, list.Count, list[i]));
            try
            {
                var r = ConvertToHtml(list[i], outputDir);
                results.Add(r);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Word转换失败] {list[i]}: {ex.Message}");
            }
        }
        progress?.Report((list.Count, list.Count, "完成"));

        return results;
    }

    /// <summary>
    /// 把 HTML 的 charset 声明改成 GB2312（hhc.exe 不支持 UTF-8）
    /// </summary>
    private static string EnsureMetaCharset(string html, string charset)
    {
        // 替换已有的 charset
        var replaced = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<meta[^>]*charset=[^>]*>",
            $"<meta http-equiv=\"Content-Type\" content=\"text/html; charset={charset}\">",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!replaced.Contains("charset=", StringComparison.OrdinalIgnoreCase))
        {
            // 在 <head> 后插入
            replaced = System.Text.RegularExpressions.Regex.Replace(
                replaced,
                @"<head[^>]*>",
                m => m.Value + $"<meta http-equiv=\"Content-Type\" content=\"text/html; charset={charset}\">",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // 同时替换 xml 声明里的 encoding
        replaced = System.Text.RegularExpressions.Regex.Replace(
            replaced,
            @"<\?xml[^>]*encoding=""[^""]*""",
            m => System.Text.RegularExpressions.Regex.Replace(m.Value, @"encoding=""[^""]*""", $"encoding=\"{charset}\""),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return replaced;
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

public class ConversionResult
{
    public string HtmlPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string ImageDirectory { get; set; } = "";
}
