using System;
using System.Windows;
using CHMGenerator.WPF.Services;

namespace CHMGenerator.WPF.Tests;

/// <summary>
/// 简单的配置测试窗口
/// 用于快速验证配置加载
/// </summary>
public partial class QuickConfigTest : Window
{
    public QuickConfigTest()
    {
        //InitializeComponent();
        TestConfig();
    }

    private void TestConfig()
    {
        var config = ToolConfiguration.Instance;

        // 获取项目根目录
        var projectRoot = GetProjectRootDirectory();
        var configPath = System.IO.Path.Combine(projectRoot, "config.json");

        var result = $@"=== 配置加载测试 ===

UsePythonConverter: {config.UsePythonConverter}
PythonToolsPath: {config.PythonToolsPath}
IsPythonAvailable(): {config.IsPythonAvailable()}

配置文件路径:
{configPath}

项目根目录:
{projectRoot}";

        MessageBox.Show(result, "配置测试", MessageBoxButton.OK, MessageBoxImage.Information);
        Console.WriteLine(result);
    }

    private static string GetProjectRootDirectory()
    {
        var currentDir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
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
}
