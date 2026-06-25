using System.IO;
using System.Text.Json;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 工具配置管理 - 简化版
/// </summary>
public class ToolConfiguration
{
    private static ToolConfiguration? _instance;
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CHMGeneratorWPF",
        "config.json");

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
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<ToolConfiguration>(json);
                if (config != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[配置] 从文件加载: {ConfigPath}");
                    System.Diagnostics.Debug.WriteLine($"[配置] UsePythonConverter = {config.UsePythonConverter}");
                    System.Diagnostics.Debug.WriteLine($"[配置] PythonToolsPath = {config.PythonToolsPath}");
                    LogManager.Instance.WriteOperation($"配置加载: UsePython={config.UsePythonConverter}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[配置] 读取失败: {ex.Message}");
        }

        // 返回默认配置，默认指向项目中的 ExternalTools
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "ExternalTools");
        System.Diagnostics.Debug.WriteLine($"[配置] 使用默认配置，工具路径: {defaultPath}");

        var defaultConfig = new ToolConfiguration
        {
            UsePythonConverter = true,
            PythonToolsPath = defaultPath
        };

        // 自动保存默认配置
        try
        {
            defaultConfig.Save();
        }
        catch { }

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