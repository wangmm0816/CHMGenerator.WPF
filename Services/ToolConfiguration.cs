using System.IO;
using System.Text.Json;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 工具配置管理 - 简化版
/// </summary>
public class ToolConfiguration
{
    private static ToolConfiguration? _instance;

    // 配置文件保存在项目根目录下（与 .csproj 同级）
    private static readonly string ConfigPath = Path.Combine(
        GetProjectRootDirectory(),
        "config.json");

    /// <summary>获取项目根目录（查找 .csproj 文件所在目录）</summary>
    private static string GetProjectRootDirectory()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

        // 从可执行文件目录向上查找，直到找到 .csproj 文件
        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.csproj").Length > 0)
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        // 如果找不到项目根目录，回退到可执行文件目录
        return AppContext.BaseDirectory;
    }

    /// <summary>是否启用 Python 转换（如果为 false，使用内置转换器）</summary>
    public bool UsePythonConverter { get; set; } = true;  // 默认启用

    /// <summary>Python 工具目录路径</summary>
    public string PythonToolsPath { get; set; } = "";

    /// <summary>获取单例实例</summary>
    public static ToolConfiguration Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Load();
            }
            return _instance;
        }
    }

    /// <summary>从配置文件加载</summary>
    private static ToolConfiguration Load()
    {
        System.Diagnostics.Debug.WriteLine($"[配置] 开始加载配置");
        System.Diagnostics.Debug.WriteLine($"[配置] 配置文件路径: {ConfigPath}");
        System.Diagnostics.Debug.WriteLine($"[配置] 项目根目录: {GetProjectRootDirectory()}");

        try
        {
            if (File.Exists(ConfigPath))
            {
                System.Diagnostics.Debug.WriteLine($"[配置] 配置文件存在，开始读取");
                var json = File.ReadAllText(ConfigPath);
                System.Diagnostics.Debug.WriteLine($"[配置] 文件内容: {json}");

                var config = JsonSerializer.Deserialize<ToolConfiguration>(json);
                if (config != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[配置] ✓ 成功加载配置");
                    System.Diagnostics.Debug.WriteLine($"[配置]   UsePythonConverter = {config.UsePythonConverter}");
                    System.Diagnostics.Debug.WriteLine($"[配置]   PythonToolsPath = {config.PythonToolsPath}");
                    LogManager.Instance.WriteOperation($"配置加载成功: UsePython={config.UsePythonConverter}, Path={config.PythonToolsPath}");
                    return config;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[配置] ✗ 反序列化返回 null");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[配置] 配置文件不存在");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[配置] ✗ 读取失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[配置]   异常类型: {ex.GetType().Name}");
        }

        // 配置文件不存在或读取失败时，返回空配置（不自动保存，避免覆盖用户设置）
        System.Diagnostics.Debug.WriteLine($"[配置] 返回空配置（需要用户在设置窗口配置）");

        var defaultConfig = new ToolConfiguration
        {
            UsePythonConverter = false,  // 默认不启用
            PythonToolsPath = ""  // 空路径，用户需要手动设置
        };

        return defaultConfig;
    }

    /// <summary>保存配置到文件</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Debug.WriteLine($"[配置] 创建目录: {dir}");
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);

            System.Diagnostics.Debug.WriteLine($"[配置] 保存成功: {ConfigPath}");
            System.Diagnostics.Debug.WriteLine($"[配置] UsePythonConverter = {UsePythonConverter}");
            System.Diagnostics.Debug.WriteLine($"[配置] PythonToolsPath = {PythonToolsPath}");

            LogManager.Instance.WriteOperation($"配置已保存: UsePython={UsePythonConverter}, Path={PythonToolsPath}");

            // 关键修复：更新单例实例，确保其他地方能立即获取到最新配置
            _instance = this;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[配置] 保存失败: {ex.Message}");
            LogManager.Instance.WriteOperation($"配置保存失败: {ex.Message}");
        }
    }

    /// <summary>检查 Python 工具是否可用</summary>
    public bool IsPythonAvailable()
    {
        if (!UsePythonConverter)
        {
            System.Diagnostics.Debug.WriteLine("[配置] Python 转换器未启用");
            return false;
        }

        if (string.IsNullOrEmpty(PythonToolsPath))
        {
            System.Diagnostics.Debug.WriteLine("[配置] Python 工具路径为空");
            return false;
        }

        // 检查关键文件
        var docToChmPath = Path.Combine(PythonToolsPath, "DocToCHM.py");
        var hyperlinkPath = Path.Combine(PythonToolsPath, "hyperlink_processor.py");

        var exists = File.Exists(docToChmPath) && File.Exists(hyperlinkPath);
        System.Diagnostics.Debug.WriteLine($"[配置] Python 工具 {(exists ? "可用" : "不可用")}: {PythonToolsPath}");

        return exists;
    }

    /// <summary>获取 DocToCHM.py 的完整路径</summary>
    public string GetDocToChmPath()
    {
        return Path.Combine(PythonToolsPath, "DocToCHM.py");
    }
}