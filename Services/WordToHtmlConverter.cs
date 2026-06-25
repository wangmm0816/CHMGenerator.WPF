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
        var htmlFileName = $"{SafeHhcFileName(name)}.html";
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

        // v3.1 修复：hhp/hhc/hhk 用 GB2312，HTML 也用 GB2312（无 BOM）
        // itss.dll 已注册后，GB2312 HTML 能正常编译
        // UTF-8 with BOM 会让 hhc.exe 4.74 产生乱码（它不读 BOM，用 ANSI 代码页）
        htmlContent = EnsureMetaCharset(htmlContent, "gb2312");

        // v2.7 修复：把 base64 内嵌图片提取为独立文件
        // hhc.exe 4.74 不支持 data: URI scheme，base64 图片会导致 HHC5003 编译失败
        var imageDirName = Path.GetFileNameWithoutExtension(htmlPath) + "_images";
        htmlContent = ExtractBase64Images(htmlContent, htmlPath, imageDirName);

        // v2.11 修复：简化 CSS（暂时禁用，先做对照实验确认根因）
        // htmlContent = SimplifyCssForHhc(htmlContent);

        // v2.8 修复：让 HTML 结构对 hhc.exe 友好（保留所有 CSS 样式）
        // OpenXmlPowerTools 输出 XHTML，hhc.exe 4.74 不喜欢 XML 声明/XHTML doctype/xmlns
        htmlContent = MakeHhcCompatible(htmlContent);

        // v3.1 修复：用 GB2312 无 BOM 写入
        // hhc.exe 4.74 用 ANSI 代码页读取，GB2312 + 系统 ANSI=GBK 能正确解析中文
        var gb2312 = Encoding.GetEncoding("GB2312",
            new EncoderReplacementFallback("?"),
            new DecoderReplacementFallback("?"));
        var htmlBytes = gb2312.GetBytes(htmlContent);
        File.WriteAllBytes(htmlPath, htmlBytes);
        Debug.WriteLine($"[Word转HTML] 写入 {htmlPath}, {htmlBytes.Length} bytes, GB2312 无 BOM");

        var fullImageDir = Path.Combine(outputDir, imageDirName);
        return new ConversionResult
        {
            HtmlPath = htmlPath,
            Title = title,
            ImageDirectory = Directory.Exists(fullImageDir) ? fullImageDir : ""
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

    /// <summary>
    /// 简化 CSS，让 hhc.exe 4.74 能稳定解析。
    /// 
    /// hhc.exe 4.74（1997 年）的 CSS 解析器问题：
    /// 1. 中文字体名（如 "微软雅黑"、"等线"、"宋体"）会让 CSS 解析器崩溃
    /// 2. 超长 CSS 块（>10000 字符）会内存溢出
    /// 3. 复杂选择器（含多个连字符 + 数字）解析不稳定
    /// 
    /// 策略：把中文字体名替换为英文 fallback（sans-serif/serif），
    ///       保留其他 CSS 属性不变。
    /// </summary>
    private static string SimplifyCssForHhc(string html)
    {
        // 1. 把 CSS 里的中文字体名替换为英文 fallback
        // font-family: 微软雅黑 → font-family: sans-serif
        // font-family: 等线 → font-family: sans-serif
        // font-family: 宋体 → font-family: serif
        // font-family: 黑体 → font-family: sans-serif
        // font-family: 楷体 → font-family: serif
        // font-family: 仿宋 → font-family: serif
        // font-family: 'Times New Roman', 'serif' → 保持不变（已是英文）
        
        var chineseFonts = new Dictionary<string, string>
        {
            { "微软雅黑", "sans-serif" },
            { "等线", "sans-serif" },
            { "黑体", "sans-serif" },
            { "宋体", "serif" },
            { "楷体", "serif" },
            { "仿宋", "serif" },
            { "仿宋_GB2312", "serif" },
            { "楷体_GB2312", "serif" },
            { "华文宋体", "serif" },
            { "华文黑体", "sans-serif" },
            { "华文楷体", "serif" },
            { "华文仿宋", "serif" },
            { "华文中宋", "serif" },
            { "华文细黑", "sans-serif" },
            { "华文新魏", "serif" },
            { "华文行楷", "serif" },
            { "隶书", "serif" },
            { "幼圆", "sans-serif" },
            { "方正姚体", "sans-serif" },
            { "方正舒体", "serif" },
        };

        foreach (var kvp in chineseFonts)
        {
            // 替换 CSS 里的中文字体名
            html = html.Replace($"font-family: {kvp.Key};", $"font-family: {kvp.Value};");
            html = html.Replace($"font-family:{kvp.Key};", $"font-family:{kvp.Value};");
            // 处理带引号的情况
            html = html.Replace($"font-family: \"{kvp.Key}\";", $"font-family: {kvp.Value};");
            html = html.Replace($"font-family:\"{kvp.Key}\";", $"font-family:{kvp.Value};");
            // 处理在字体列表中的情况：font-family: 微软雅黑, sans-serif
            html = html.Replace($"{kvp.Key}, ", "");
            html = html.Replace($"{kvp.Key},", "");
        }

        // 2. 删除 CSS 里的 content: 属性（hhc.exe 不支持）
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"content\s*:\s*[^;]+;",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 3. 删除 CSS 注释 /* ... */
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"/\*[^*]*\*+(?:[^/*][^*]*\*+)*/",
            "",
            System.Text.RegularExpressions.RegexOptions.None);

        Debug.WriteLine($"[Word转HTML] SimplifyCssForHhc 完成，HTML 长度: {html.Length}");
        return html;
    }

    /// <summary>
    /// 让 HTML 结构对 hhc.exe 4.74（1997 年的老软件）友好。
    /// 关键：保留所有 CSS 样式，只做最小化的结构性修改。
    /// 
    /// 修改项：
    /// 1. 删除 &lt;?xml ... ?&gt; 声明（hhc.exe 期望直接 &lt;!DOCTYPE&gt; 开头）
    /// 2. 把 XHTML 1.0 doctype 换成 HTML 4.01 Transitional（hhc.exe 4.74 只认 HTML 3.2/4.0）
    /// 3. 删除 &lt;html&gt; 上的 xmlns 属性
    /// 4. 把 &lt;br /&gt; &lt;img /&gt; &lt;hr /&gt; 等自闭合标签转成 &lt;br&gt; &lt;img&gt; &lt;hr&gt;
    /// 5. CSS 一律保留（包括 &lt;style&gt; 块和 inline style="..."）
    /// </summary>
    private static string MakeHhcCompatible(string html)
    {
        // 1. 删除 XML 声明
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<\?xml[^>]*\?>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2. 替换 doctype
        // 任意 doctype → HTML 4.01 Transitional
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<!DOCTYPE[^>]*>",
            @"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.01 Transitional//EN"" ""http://www.w3.org/TR/html4/loose.dtd"">",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2.5 关键修复：如果 HTML 开头没有 doctype（OpenXmlPowerTools 可能不输出 doctype），
        // 强制在最前面插入一个
        var trimmedStart = html.TrimStart();
        if (!trimmedStart.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
            !trimmedStart.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase))
        {
            html = @"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.01 Transitional//EN"" ""http://www.w3.org/TR/html4/loose.dtd"">" + "\r\n" + html;
        }

        // 3. 删除 <html> 上的 xmlns 属性
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<html[^>]*>",
            "<html>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 4. 把自闭合标签转成 HTML 4 风格
        // <br /> → <br>, <img ... /> → <img ...>, <hr /> → <hr>, <input ... /> → <input ...>
        // 注意：要在 <img src="..." /> 这种情况下保留属性
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<br\s*/>",
            "<br>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<hr\s*/>",
            "<hr>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // <img ... /> → <img ...>
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<img([^>]*?)\s*/>",
            "<img$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // <input ... /> → <input ...>
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<input([^>]*?)\s*/>",
            "<input$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // <col ... /> → <col ...>
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<col([^>]*?)\s*/>",
            "<col$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // <link ... /> → <link ...>
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<link([^>]*?)\s*/>",
            "<link$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // <meta ... /> → <meta ...>
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<meta([^>]*?)\s*/>",
            "<meta$1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 5. 删除 HTML 中的空 xmlns:xsi、xmlns:oox 等命名空间声明（OpenXmlPowerTools 偶尔会加）
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"\s+xmlns(:[a-zA-Z]+)?=""[^""]*""",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 6. 折叠开头的多余空行
        html = html.TrimStart();

        Debug.WriteLine($"[Word转HTML] MakeHhcCompatible 完成，最终 HTML 长度: {html.Length}");
        return html;
    }

    /// <summary>
    /// 把 HTML 里的 base64 data URI 图片提取为独立文件，替换成相对路径。
    /// 
    /// 原因：hhc.exe 4.74（1997 年）不支持 data: URI scheme，
    /// base64 内嵌图片会导致 HHC5003 编译失败。
    /// 而且 base64 会让 HTML 文件变成几 MB 甚至几十 MB，hhc.exe 对超大文件不友好。
    /// 
    /// 提取后：
    /// - 图片保存到 {html所在目录}/{imageDirName}/ 目录
    /// - HTML 里的 src="data:image/png;base64,XXX" 改成 src="{imageDirName}/img_001.png"
    /// - CHM 编译时图片会被打包进去
    /// </summary>
    private static string ExtractBase64Images(string html, string htmlPath, string imageDirName)
    {
        var htmlDir = Path.GetDirectoryName(htmlPath) ?? "";
        var htmlBaseName = Path.GetFileNameWithoutExtension(htmlPath);
        var fullImageDir = Path.Combine(htmlDir, imageDirName);

        // 正则匹配 src="data:image/xxx;base64,XXX" 或 src='data:image/xxx;base64,XXX'
        var regex = new System.Text.RegularExpressions.Regex(
            @"src=[""']data:image/(?<ext>png|jpe?g|gif|bmp|x-?emf|wmf|tiff?);base64,(?<data>[^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        int imgIndex = 1;
        bool dirCreated = false;

        string result = regex.Replace(html, m =>
        {
            var ext = m.Groups["ext"].Value.ToLowerInvariant();
            if (ext == "jpeg") ext = "jpg";
            if (ext == "x-emf") ext = "emf";
            if (ext == "tiff") ext = "tif";

            var base64Data = m.Groups["data"].Value;

            try
            {
                if (!dirCreated)
                {
                    Directory.CreateDirectory(fullImageDir);
                    dirCreated = true;
                }

                var imgFileName = $"{htmlBaseName}_{imgIndex:D3}.{ext}";
                var imgPath = Path.Combine(fullImageDir, imgFileName);

                var bytes = Convert.FromBase64String(base64Data);
                File.WriteAllBytes(imgPath, bytes);

                imgIndex++;
                Debug.WriteLine($"[Word转HTML] 提取图片 {imgFileName} ({bytes.Length} bytes)");

                return $"src=\"{imageDirName}/{imgFileName}\"";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Word转HTML] 图片提取失败: {ex.Message}");
                return m.Value; // 失败则保留原 data URI
            }
        });

        Debug.WriteLine($"[Word转HTML] 共提取 {imgIndex - 1} 张图片到 {imageDirName}/");
        return result;
    }

    /// <summary>
    /// 把文件名转换成 hhc.exe 能稳定编译的安全文件名。
    /// hhc.exe 4.74 对文件名兼容性极差：
    ///   - 半角括号 () [] {} 会被当作参数分隔符
    ///   - 空格会让路径解析被截断
    ///   - 多个点 . 让 hhc.exe 误判扩展名
    /// 保留：中文、字母数字、下划线、连字符、单个点
    /// </summary>
    private static string SafeHhcFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "untitled";

        // 修复 v2.5 的 Bug：保留最后一个点（扩展名分隔符），其他点替换为下划线
        // 旧逻辑只保留第一个点，导致 .html 的点被替换成下划线，hhc.exe 不认
        int lastDotIndex = name.LastIndexOf('.');

        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == '.')
            {
                if (i == lastDotIndex && lastDotIndex > 0)
                    sb.Append(c);
                else
                    sb.Append('_');
                continue;
            }
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
            sb.Append(c);
        }

        var result = sb.ToString();
        while (result.Contains("__")) result = result.Replace("__", "_");
        result = result.Trim('_');
        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }
}

public class ConversionResult
{
    public string HtmlPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string ImageDirectory { get; set; } = "";
}
