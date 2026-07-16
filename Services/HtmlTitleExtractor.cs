using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 从 HTML 文件中提取 &lt;title&gt;
/// </summary>
public static class HtmlTitleExtractor
{
    public static string Extract(string htmlPath)
    {
        if (!File.Exists(htmlPath)) return "";

        // 尝试用多种编码读取
        string content;
        try
        {
            // 先读前 4KB 检测编码声明
            var bytes = new byte[Math.Min(4096, new FileInfo(htmlPath).Length)];
            using (var fs = File.OpenRead(htmlPath))
            {
                fs.Read(bytes, 0, bytes.Length);
            }
            var head = Encoding.ASCII.GetString(bytes);

            Encoding enc = Encoding.UTF8;
            if (head.Contains("charset=gb2312", StringComparison.OrdinalIgnoreCase) ||
                head.Contains("charset=gbk", StringComparison.OrdinalIgnoreCase))
            {
                enc = Encoding.GetEncoding("GB2312");
            }
            else if (head.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase))
            {
                enc = Encoding.UTF8;
            }

            using (var fs = File.OpenRead(htmlPath))
            using (var reader = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true))
            {
                content = reader.ReadToEnd();
            }
        }
        catch
        {
            return "";
        }

        return ExtractFromContent(content);
    }

    public static string ExtractFromContent(string htmlContent)
    {
        if (string.IsNullOrEmpty(htmlContent)) return string.Empty;

        var match = Regex.Match(htmlContent, @"<title\b[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success) return string.Empty;

        var title = match.Groups[1].Value;

        // 使用 WebUtility.HtmlDecode 解码所有 HTML 实体
        // 支持常见的如 &nbsp; &lt; &gt; &amp; &quot; &#39; 以及数字实体如 &#160; &#60; 等
        title = WebUtility.HtmlDecode(title);

        // 规范化空白字符
        title = Regex.Replace(title, @"\s+", " ").Trim();

        return title;
    }
}
