using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CHMGenerator.WPF.Models;

/// <summary>
/// 文档节点类型
/// </summary>
public enum NodeType
{
    /// <summary>文件夹</summary>
    Folder,
    /// <summary>HTML 文件</summary>
    Html,
    /// <summary>Word 文件（待转换）</summary>
    Word,
    /// <summary>转换后的 HTML 文件</summary>
    ConvertedHtml
}

/// <summary>
/// 文档树节点（文件夹或文件）
/// </summary>
public class DocumentNode : INotifyPropertyChanged
{
    private string _title = "";
    private string _originalTitle = "";
    private string _sourcePath = "";
    private string _convertedHtmlPath = "";
    private NodeType _nodeType = NodeType.Html;
    private bool _isExpanded = true;
    private bool _isSelected;
    private ImageSource? _icon;
    private DocumentNode? _parent;
    private int _order;

    /// <summary>节点在父级中的顺序</summary>
    public int Order
    {
        get => _order;
        set { _order = value; OnPropertyChanged(); }
    }

    /// <summary>显示标题（在 CHM 目录中显示的名称）</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>原始标题（从 &lt;title&gt; 提取的，用于恢复）</summary>
    public string OriginalTitle
    {
        get => _originalTitle;
        set { _originalTitle = value; OnPropertyChanged(); }
    }

    /// <summary>源文件路径（HTML 或 Word）</summary>
    public string SourcePath
    {
        get => _sourcePath;
        set { _sourcePath = value; OnPropertyChanged(); }
    }

    /// <summary>转换后的 HTML 路径（Word 转换后使用）</summary>
    public string ConvertedHtmlPath
    {
        get => _convertedHtmlPath;
        set { _convertedHtmlPath = value; OnPropertyChanged(); }
    }

    /// <summary>最终用于编译的 HTML 路径</summary>
    public string EffectiveHtmlPath => NodeType == NodeType.Word ? ConvertedHtmlPath : SourcePath;

    /// <summary>节点类型</summary>
    public NodeType NodeType
    {
        get => _nodeType;
        set
        {
            if (_nodeType != value)
            {
                _nodeType = value;
                OnPropertyChanged();
                UpdateIcon();
            }
        }
    }

    /// <summary>是否展开</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>是否选中</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>图标</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    /// <summary>父节点</summary>
    public DocumentNode? Parent
    {
        get => _parent;
        set { _parent = value; OnPropertyChanged(); }
    }

    /// <summary>子节点集合</summary>
    public ObservableCollection<DocumentNode> Children { get; } = new();

    /// <summary>是否是文件夹</summary>
    public bool IsFolder => NodeType == NodeType.Folder;

    /// <summary>是否是文件</summary>
    public bool IsFile => !IsFolder;

    /// <summary>节点深度</summary>
    public int Depth
    {
        get
        {
            int d = 0;
            var p = Parent;
            while (p != null) { d++; p = p.Parent; }
            return d;
        }
    }

    /// <summary>相对于根节点的相对路径（用于生成 .hhc）</summary>
    public string RelativePath
    {
        get
        {
            var parts = new List<string>();
            var current = this;

            // 首先处理当前节点（最内层）
            if (current != null && !string.IsNullOrEmpty(current.Title))
            {
                if (current.IsFolder)
                {
                    parts.Add(SanitizeFileName(current.Title));
                }
                else if (current.NodeType == NodeType.Word)
                {
                    // Word 文件：需要包含 Python 生成的目录结构
                    // ConvertedHtmlPath 格式：outputDir/html/产品说明书/chapter_1/chapter_1.html
                    // 需要提取：产品说明书/chapter_1/chapter_1.html
                    var htmlPath = current.EffectiveHtmlPath;
                    if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
                    {
                        // 找到 html 目录
                        var dir = Path.GetDirectoryName(htmlPath);
                        var pathSegments = new List<string>();
                        pathSegments.Insert(0, Path.GetFileName(htmlPath));

                        // 向上遍历，直到找到 "html" 目录
                        while (!string.IsNullOrEmpty(dir))
                        {
                            var dirName = Path.GetFileName(dir);
                            if (dirName.Equals("html", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }
                            pathSegments.Insert(0, dirName);
                            dir = Path.GetDirectoryName(dir);
                        }

                        // pathSegments 现在包含：[产品说明书, chapter_1, chapter_1.html]
                        parts.Add(string.Join("/", pathSegments));
                    }
                    else
                    {
                        parts.Add($"{SanitizeFileName(current.Title)}.html");
                    }
                }
                else
                {
                    // 普通 HTML 文件
                    var htmlPath = current.EffectiveHtmlPath;
                    if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
                    {
                        parts.Add(Path.GetFileName(htmlPath));
                    }
                    else
                    {
                        var fileName = SanitizeFileName(current.Title);
                        if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                            !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName += ".html";
                        }
                        parts.Add(fileName);
                    }
                }

                // 向上遍历父节点（只添加文件夹）
                current = current.Parent;
                while (current != null)
                {
                    if (!string.IsNullOrEmpty(current.Title))
                    {
                        parts.Insert(0, SanitizeFileName(current.Title));
                    }
                    current = current.Parent;
                }
            }

            return string.Join("/", parts);
        }
    }

    /// <summary>递归获取所有文件节点（包括自己如果是文件）</summary>
    public IEnumerable<DocumentNode> GetAllFileNodes()
    {
        // 如果自己是文件节点，先返回自己
        if (!IsFolder)
        {
            yield return this;
        }

        // 然后遍历子节点
        foreach (var child in Children)
        {
            if (child.IsFolder)
            {
                foreach (var f in child.GetAllFileNodes()) yield return f;
            }
            else
            {
                yield return child;
            }
        }
    }

    /// <summary>递归获取所有节点（包括自己）</summary>
    public IEnumerable<DocumentNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var d in child.DescendantsAndSelf()) yield return d;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    private void UpdateIcon()
    {
        // 图标在 View 层根据 NodeType 通过 Converter 绑定
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// CHM 工程配置
/// </summary>
public class ChmProject : INotifyPropertyChanged
{
    private string _title = "帮助文档";
    private string _outputDirectory = "";
    private string _defaultTopic = "";
    private bool _fullTextSearch = true;
    private bool _binaryToc = true;
    private bool _autoIndex = true;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set { _outputDirectory = value; OnPropertyChanged(); }
    }

    public string DefaultTopic
    {
        get => _defaultTopic;
        set { _defaultTopic = value; OnPropertyChanged(); }
    }

    public bool FullTextSearch
    {
        get => _fullTextSearch;
        set { _fullTextSearch = value; OnPropertyChanged(); }
    }

    public bool BinaryToc
    {
        get => _binaryToc;
        set { _binaryToc = value; OnPropertyChanged(); }
    }

    public bool AutoIndex
    {
        get => _autoIndex;
        set { _autoIndex = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
