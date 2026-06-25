using System;
using System.IO;
using System.Text.Json;
using CHMGenerator.WPF.Services;

namespace CHMGenerator.WPF.Tests;

/// <summary>
/// 配置诊断工具
/// 用于排查配置保存和加载的问题
/// 使用方法：注释掉其他测试类的 Main 方法，然后运行此类
/// </summary>
public class ConfigDiagnostics
{
    // 注释掉 Main 方法，避免多个入口点冲突
    // 如果要运行此诊断工具，取消注释并注释掉其他测试类的 Main 方法
    /*
    public static void Main(string[] args)
    {
        RunDiagnostics();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
    */

    public static void RunDiagnostics()
    {
        Console.WriteLine("=== CHM Generator WPF - 配置诊断工具 ===\n");

        // 1. 检查配置文件路径
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CHMGeneratorWPF",
            "config.json");

        Console.WriteLine($"1. 配置文件路径: {configPath}");
        Console.WriteLine($"   是否存在: {File.Exists(configPath)}");
        Console.WriteLine();

        // 2. 读取配置文件内容
        if (File.Exists(configPath))
        {
            Console.WriteLine("2. 配置文件内容:");
            try
            {
                var content = File.ReadAllText(configPath);
                Console.WriteLine(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   读取失败: {ex.Message}");
            }
            Console.WriteLine();
        }

        // 3. 测试配置加载
        Console.WriteLine("3. 测试配置加载:");
        try
        {
            var config = ToolConfiguration.Instance;
            Console.WriteLine($"   UsePythonConverter: {config.UsePythonConverter}");
            Console.WriteLine($"   PythonToolsPath: {config.PythonToolsPath}");
            Console.WriteLine($"   IsPythonAvailable(): {config.IsPythonAvailable()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   加载失败: {ex.Message}");
        }
        Console.WriteLine();

        // 4. 检查 Python 工具目录
        Console.WriteLine("4. 检查 Python 工具目录:");
        var config2 = ToolConfiguration.Instance;
        if (!string.IsNullOrEmpty(config2.PythonToolsPath))
        {
            Console.WriteLine($"   路径: {config2.PythonToolsPath}");
            Console.WriteLine($"   目录存在: {Directory.Exists(config2.PythonToolsPath)}");

            if (Directory.Exists(config2.PythonToolsPath))
            {
                var docToChmPath = Path.Combine(config2.PythonToolsPath, "DocToCHM.py");
                var hyperlinkPath = Path.Combine(config2.PythonToolsPath, "hyperlink_processor.py");

                Console.WriteLine($"   DocToCHM.py 存在: {File.Exists(docToChmPath)}");
                Console.WriteLine($"   hyperlink_processor.py 存在: {File.Exists(hyperlinkPath)}");
            }
        }
        else
        {
            Console.WriteLine("   路径为空");
        }
        Console.WriteLine();

        // 5. 测试保存配置
        Console.WriteLine("5. 测试保存配置:");
        Console.WriteLine("   是否要测试保存配置？(y/n)");
        var input = Console.ReadLine();
        if (input?.ToLower() == "y")
        {
            try
            {
                var config3 = ToolConfiguration.Instance;
                Console.WriteLine($"   当前配置:");
                Console.WriteLine($"     UsePythonConverter: {config3.UsePythonConverter}");
                Console.WriteLine($"     PythonToolsPath: {config3.PythonToolsPath}");

                Console.WriteLine("\n   保存配置中...");
                config3.Save();

                Console.WriteLine("   重新加载配置...");
                // 强制重新读取
                var jsonContent = File.ReadAllText(configPath);
                var reloaded = JsonSerializer.Deserialize<ToolConfiguration>(jsonContent);

                Console.WriteLine($"   重新加载后:");
                Console.WriteLine($"     UsePythonConverter: {reloaded?.UsePythonConverter}");
                Console.WriteLine($"     PythonToolsPath: {reloaded?.PythonToolsPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   测试失败: {ex.Message}");
            }
        }
        Console.WriteLine();

        Console.WriteLine("=== 诊断完成 ===");
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}
