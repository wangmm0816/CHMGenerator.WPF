using System.IO;
using System.Windows;
using CHMGenerator.WPF.Services;
using Microsoft.Win32;

namespace CHMGenerator.WPF.Views;

public partial class SettingsWindow : Window
{
    private readonly ToolConfiguration _config;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = ToolConfiguration.Instance;
        LoadConfig();
    }

    private void LoadConfig()
    {
        UsePythonCheckBox.IsChecked = _config.UsePythonConverter;
        PythonPathTextBox.Text = _config.PythonToolsPath;

        UpdateStatus();
    }

    private void UpdateStatus(object? sender = null, RoutedEventArgs? e = null)
    {
        if (UsePythonCheckBox.IsChecked == true)
        {
            var toolsPath = PythonPathTextBox.Text;
            if (Directory.Exists(toolsPath))
            {
                // 检查关键文件
                var docToChmPath = Path.Combine(toolsPath, "DocToCHM.py");
                var hyperlinkPath = Path.Combine(toolsPath, "hyperlink_processor.py");

                if (File.Exists(docToChmPath) && File.Exists(hyperlinkPath))
                {
                    PythonStatusText.Text = "状态：✓ 已启用，工具文件完整";
                    PythonStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    PythonStatusText.Text = "状态：⚠ 已启用，但工具文件不完整";
                    PythonStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            else if (!string.IsNullOrEmpty(toolsPath))
            {
                PythonStatusText.Text = "状态：✗ 已启用，但目录不存在";
                PythonStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                PythonStatusText.Text = "状态：⚠ 已启用，但未设置路径";
                PythonStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        else
        {
            PythonStatusText.Text = "状态：未启用（将使用内置转换器）";
            PythonStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        }
    }

    private void PythonPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void BrowsePythonPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择 Python 工具目录（包含 DocToCHM.py）",
            InitialDirectory = string.IsNullOrEmpty(PythonPathTextBox.Text)
                ? Path.Combine(AppContext.BaseDirectory, "ExternalTools")
                : PythonPathTextBox.Text
        };

        if (dlg.ShowDialog() == true)
        {
            PythonPathTextBox.Text = dlg.FolderName;
            UpdateStatus();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 获取UI中的值
            var usePython = UsePythonCheckBox.IsChecked == true;
            var pythonPath = PythonPathTextBox.Text;

            // 调试输出：保存前
            System.Diagnostics.Debug.WriteLine("");
            System.Diagnostics.Debug.WriteLine("================================================");
            System.Diagnostics.Debug.WriteLine("=== 设置窗口：开始保存配置 ===");
            System.Diagnostics.Debug.WriteLine($"UI 勾选框: {usePython}");
            System.Diagnostics.Debug.WriteLine($"UI 路径框: {pythonPath}");
            System.Diagnostics.Debug.WriteLine($"配置对象引用: {_config.GetHashCode()}");

            // 更新配置对象
            _config.UsePythonConverter = usePython;
            _config.PythonToolsPath = pythonPath;

            // 调试输出：赋值后
            System.Diagnostics.Debug.WriteLine($"赋值后 - UsePythonConverter: {_config.UsePythonConverter}");
            System.Diagnostics.Debug.WriteLine($"赋值后 - PythonToolsPath: {_config.PythonToolsPath}");

            // 保存到文件
            System.Diagnostics.Debug.WriteLine("调用 Save() 方法...");
            _config.Save();
            System.Diagnostics.Debug.WriteLine("Save() 方法执行完成");

            // 直接读取配置文件验证
            var projectRoot = GetProjectRootDirectory();
            var configFilePath = Path.Combine(projectRoot, "config.json");
            System.Diagnostics.Debug.WriteLine($"配置文件路径: {configFilePath}");

            if (File.Exists(configFilePath))
            {
                var fileContent = File.ReadAllText(configFilePath);
                System.Diagnostics.Debug.WriteLine($"配置文件内容:\n{fileContent}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠ 配置文件不存在！");
            }

            // 验证单例
            var reloadedConfig = ToolConfiguration.Instance;
            System.Diagnostics.Debug.WriteLine($"单例引用: {reloadedConfig.GetHashCode()}");
            System.Diagnostics.Debug.WriteLine($"单例 UsePythonConverter: {reloadedConfig.UsePythonConverter}");
            System.Diagnostics.Debug.WriteLine($"单例 PythonToolsPath: {reloadedConfig.PythonToolsPath}");
            System.Diagnostics.Debug.WriteLine($"单例 IsPythonAvailable: {reloadedConfig.IsPythonAvailable()}");
            System.Diagnostics.Debug.WriteLine("================================================");
            System.Diagnostics.Debug.WriteLine("");

            MessageBox.Show(
                "配置已保存！\n\n" +
                $"Python 转换器：{(_config.UsePythonConverter ? "启用" : "未启用")}\n" +
                $"工具路径：{_config.PythonToolsPath}\n" +
                $"配置文件：{configFilePath}\n" +
                $"状态检查：{(_config.IsPythonAvailable() ? "✓ 可用" : "✗ 不可用")}\n\n" +
                "配置立即生效，下次生成 CHM 时将使用新配置。",
                "保存成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"✗ 保存配置时发生异常: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"堆栈跟踪:\n{ex.StackTrace}");

            MessageBox.Show(
                $"保存配置失败！\n\n错误信息：{ex.Message}\n\n请检查日志或联系开发者。",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string GetProjectRootDirectory()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.csproj").Length > 0)
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
