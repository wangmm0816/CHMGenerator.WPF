using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Helpers;

/// <summary>
/// bool → Visibility
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public static readonly BooleanToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// bool 反转
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>
/// 非空字符串 → Visible
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 根据节点类型返回 emoji 字符
/// </summary>
public class NodeTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NodeType nt)
        {
            return nt switch
            {
                NodeType.Folder => "📁",
                NodeType.Html => "🌐",
                NodeType.Word => "📝",
                NodeType.ConvertedHtml => "✅",
                NodeType.ApiHtmlRoot => "📦",
                NodeType.ApiHtml => "📘",
                _ => "📄"
            };
        }
        return "📄";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 节点状态文字
/// </summary>
public class NodeTypeToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NodeType nt)
        {
            return nt switch
            {
                NodeType.Folder => "文件夹",
                NodeType.Html => "HTML 文件",
                NodeType.Word => "Word 文档 (待自动转换)",
                NodeType.ConvertedHtml => "HTML 文件 (已转换)",
                NodeType.ApiHtmlRoot => "API HTML 目录",
                NodeType.ApiHtml => "API HTML 文件",
                _ => "未知"
            };
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 路径截断显示
/// </summary>
public class PathShortenerConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
        {
            if (s.Length <= 60) return s;
            return "..." + s.Substring(s.Length - 57);
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
