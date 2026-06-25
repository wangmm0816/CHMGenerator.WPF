using System;
using System.IO;
using System.Threading.Tasks;
using CHMGenerator.WPF.Services;

namespace CHMGenerator.WPF.Tests;

/// <summary>
/// 外部工具集成测试脚本
/// 用于验证 Python doc2html 是否正常工作
/// 使用方法：注释掉其他测试类的 Main 方法，然后运行此类
/// </summary>
public class ExternalToolsTest
{
    // 注释掉 Main 方法，避免多个入口点冲突
    // 如果要运行此测试，取消注释并注释掉其他测试类的 Main 方法
    /*
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== CHM Generator WPF - 外部工具集成测试 ===\n");

        // 测试配置管理
        TestConfiguration();

        // 测试 Python doc2html（如果启用）
        await TestPythonDoc2Html();

        Console.WriteLine("\n=== 测试完成 ===");
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
    */

    public static async Task RunTests()
    {
        Console.WriteLine("=== CHM Generator WPF - 外部工具集成测试 ===\n");

        // 测试配置管理
        TestConfiguration();

        // 测试 Python doc2html（如果启用）
        await TestPythonDoc2Html();

        Console.WriteLine("\n=== 测试完成 ===");
    }

    private static void TestConfiguration()
    {
        Console.WriteLine("--- 测试 1: 配置管理 ---");
        try
        {
            var config = ToolConfiguration.Instance;
            Console.WriteLine($"Python 转换器启用: {config.UsePythonConverter}");
            Console.WriteLine($"Python 工具路径: {config.PythonToolsPath}");
            Console.WriteLine($"Python 可用: {config.IsPythonAvailable()}");
            Console.WriteLine();

            Console.WriteLine("✓ 配置管理测试通过");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ 配置管理测试失败: {ex.Message}");
        }
        Console.WriteLine();
    }

    private static async Task TestPythonDoc2Html()
    {
        Console.WriteLine("--- 测试 2: Python doc2html 转换 ---");
        var config = ToolConfiguration.Instance;

        if (!config.IsPythonAvailable())
        {
            Console.WriteLine("⊘ 跳过：Python doc2html 未启用或不可用");
            Console.WriteLine();
            return;
        }

        try
        {
            // 查找一个测试用的 Word 文件
            var testDocxPath = FindTestDocx();
            if (string.IsNullOrEmpty(testDocxPath))
            {
                Console.WriteLine("⊘ 跳过：未找到测试用的 .docx 文件");
                Console.WriteLine("  提示：在项目目录下放置一个 test.docx 文件");
                Console.WriteLine();
                return;
            }

            Console.WriteLine($"测试文件: {testDocxPath}");
            var outputDir = Path.Combine(Path.GetTempPath(), $"CHMGenTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);

            var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
            var title = Path.GetFileNameWithoutExtension(testDocxPath);

            Console.WriteLine("开始转换...");
            var result = await ExternalToolsIntegration.ConvertWordToPythonHtml(
                config.PythonToolsPath,
                testDocxPath,
                outputDir,
                title,
                progress);

            if (result.Success && !string.IsNullOrEmpty(result.HtmlDirectory))
            {
                Console.WriteLine($"✓ Python 转换测试通过");
                Console.WriteLine($"  HTML 目录: {result.HtmlDirectory}");
                Console.WriteLine($"  配置文件: {result.TxtConfigFile}");

                // 统计生成的文件
                var htmlFiles = Directory.GetFiles(result.HtmlDirectory, "*.html", SearchOption.AllDirectories);
                Console.WriteLine($"  生成 HTML 文件数: {htmlFiles.Length}");

                if (htmlFiles.Length > 0)
                {
                    Console.WriteLine($"  主文件: {htmlFiles[0]}");
                    Console.WriteLine($"  大小: {new FileInfo(htmlFiles[0]).Length} bytes");
                }
            }
            else
            {
                Console.WriteLine($"✗ Python 转换测试失败");
                Console.WriteLine($"  错误: {result.ErrorMessage}");
            }

            // 清理临时目录
            try { Directory.Delete(outputDir, true); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Python 转换测试失败: {ex.Message}");
        }
        Console.WriteLine();
    }

    private static string? FindTestDocx()
    {
        // 在当前目录和常见位置查找测试文件
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "test.docx"),
            Path.Combine(Directory.GetCurrentDirectory(), "example.docx"),
            @"D:\NetCode\CHMGenerator\FAQ.docx",
            @"D:\python\doc2html\test_internal.docx"
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
