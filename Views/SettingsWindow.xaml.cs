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
        _config.UsePythonConverter = UsePythonCheckBox.IsChecked == true;
        _config.PythonToolsPath = PythonPathTextBox.Text;

        _config.Save();

        MessageBox.Show(
            "配置已保存！\n\n" +
            $"Python 转换器：{(_config.UsePythonConverter ? "启用" : "未启用")}\n" +
            $"工具路径：{_config.PythonToolsPath}\n\n" +
            "下次生成 CHM 时将使用新配置。",
            "成功",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
