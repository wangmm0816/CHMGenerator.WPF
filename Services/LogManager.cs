using System.Diagnostics;
using System.IO;
using System.Text;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 日志管理服务
/// 自动将操作日志、编译日志和调试日志保存到 log 目录的不同子文件夹
/// </summary>
public class LogManager
{
    private static LogManager? _instance;
    private readonly string _logRootDirectory;
    private readonly string _statusLogDirectory;
    private readonly string _compileLogDirectory;
    private readonly string _debugLogDirectory;
    private string? _currentStatusLogPath;
    private string? _currentCompileLogPath;
    private string? _currentDebugLogPath;
    private readonly object _lockObject = new();

    private LogManager()
    {
        // 在项目目录下创建 logs 文件夹及子文件夹（保持原有的 logs 目录名）
        _logRootDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        _statusLogDirectory = Path.Combine(_logRootDirectory, "Status");
        _compileLogDirectory = Path.Combine(_logRootDirectory, "Compile");
        _debugLogDirectory = Path.Combine(_logRootDirectory, "Debug");

        Directory.CreateDirectory(_statusLogDirectory);
        Directory.CreateDirectory(_compileLogDirectory);
        Directory.CreateDirectory(_debugLogDirectory);

        // 设置 Debug 输出重定向
        Trace.Listeners.Add(new DebugTraceListener(this));
    }

    public static LogManager Instance => _instance ??= new LogManager();

    /// <summary>
    /// 开始新的会话，创建新的日志文件
    /// </summary>
    public void StartNewSession()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentStatusLogPath = Path.Combine(_statusLogDirectory, $"status_{timestamp}.txt");
        _currentCompileLogPath = Path.Combine(_compileLogDirectory, $"compile_{timestamp}.txt");
        _currentDebugLogPath = Path.Combine(_debugLogDirectory, $"debug_{timestamp}.txt");

        // 创建日志文件并写入头部信息
        var header = $"=== CHM Generator 日志 ===\n会话开始时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";

        lock (_lockObject)
        {
            File.WriteAllText(_currentStatusLogPath, header, Encoding.UTF8);
            File.WriteAllText(_currentCompileLogPath, header, Encoding.UTF8);
            File.WriteAllText(_currentDebugLogPath, header, Encoding.UTF8);
        }

        WriteStatus($"日志文件已创建");
        WriteDebug($"调试日志文件: {Path.GetFileName(_currentDebugLogPath)}");
    }

    /// <summary>
    /// 写入状态日志（主页面左下角状态栏的内容）
    /// </summary>
    public void WriteStatus(string message)
    {
        if (string.IsNullOrEmpty(_currentStatusLogPath)) StartNewSession();

        var logEntry = $"[{DateTime.Now:HH:mm:ss}] [状态栏] {message}\n";

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_currentStatusLogPath!, logEntry, Encoding.UTF8);
            }
            catch
            {
                // 写入失败，忽略
            }
        }
    }

    /// <summary>
    /// 写入操作日志（用户操作记录，如添加文件、删除节点等）
    /// </summary>
    public void WriteOperation(string message)
    {
        if (string.IsNullOrEmpty(_currentStatusLogPath)) StartNewSession();

        var logEntry = $"[{DateTime.Now:HH:mm:ss}] [操作] {message}\n";

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_currentStatusLogPath!, logEntry, Encoding.UTF8);
            }
            catch
            {
                // 写入失败，忽略
            }
        }
    }

    /// <summary>
    /// 写入调试日志（System.Diagnostics.Debug.WriteLine 的内容）
    /// </summary>
    public void WriteDebug(string message)
    {
        if (string.IsNullOrEmpty(_currentDebugLogPath)) StartNewSession();

        var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_currentDebugLogPath!, logEntry, Encoding.UTF8);
            }
            catch
            {
                // 写入失败，忽略
            }
        }
    }

    /// <summary>
    /// 写入编译日志
    /// </summary>
    public void WriteCompile(string message)
    {
        if (string.IsNullOrEmpty(_currentCompileLogPath)) StartNewSession();

        var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_currentCompileLogPath!, logEntry, Encoding.UTF8);
            }
            catch
            {
                // 写入失败，忽略
            }
        }
    }

    /// <summary>
    /// 写入异常日志
    /// </summary>
    public void WriteException(Exception ex)
    {
        var exceptionLog = new StringBuilder();
        exceptionLog.AppendLine($"=== 异常信息 ===");
        exceptionLog.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        exceptionLog.AppendLine($"消息: {ex.Message}");
        exceptionLog.AppendLine($"类型: {ex.GetType().FullName}");
        exceptionLog.AppendLine($"堆栈跟踪:");
        exceptionLog.AppendLine(ex.StackTrace);

        if (ex.InnerException != null)
        {
            exceptionLog.AppendLine();
            exceptionLog.AppendLine($"内部异常: {ex.InnerException.Message}");
            exceptionLog.AppendLine(ex.InnerException.StackTrace);
        }

        exceptionLog.AppendLine();

        WriteDebug(exceptionLog.ToString());
    }

    /// <summary>
    /// 获取当前状态日志文件路径
    /// </summary>
    public string? GetCurrentStatusLogPath() => _currentStatusLogPath;

    /// <summary>
    /// 获取当前调试日志文件路径
    /// </summary>
    public string? GetCurrentDebugLogPath() => _currentDebugLogPath;

    /// <summary>
    /// 获取当前编译日志文件路径
    /// </summary>
    public string? GetCurrentCompileLogPath() => _currentCompileLogPath;

    /// <summary>
    /// 获取日志根目录路径
    /// </summary>
    public string GetLogDirectory() => _logRootDirectory;

    /// <summary>
    /// 清理旧日志文件（保留最近 30 天）
    /// </summary>
    public void CleanOldLogs(int keepDays = 30)
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-keepDays);

            CleanDirectory(_statusLogDirectory, cutoffDate);
            CleanDirectory(_compileLogDirectory, cutoffDate);
            CleanDirectory(_debugLogDirectory, cutoffDate);
        }
        catch
        {
            // 清理失败，忽略
        }
    }

    private void CleanDirectory(string directory, DateTime cutoffDate)
    {
        if (!Directory.Exists(directory)) return;

        var logFiles = Directory.GetFiles(directory, "*.txt");

        foreach (var file in logFiles)
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.LastWriteTime < cutoffDate)
            {
                try
                {
                    File.Delete(file);
                    WriteDebug($"已删除旧日志: {Path.GetFileName(file)}");
                }
                catch
                {
                    // 删除失败，忽略
                }
            }
        }
    }

    /// <summary>
    /// 打开日志目录
    /// </summary>
    public void OpenLogDirectory()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _logRootDirectory);
        }
        catch (Exception ex)
        {
            WriteDebug($"打开日志目录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 结束当前会话
    /// </summary>
    public void EndSession()
    {
        WriteStatus($"会话结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteStatus("===================\n");

        WriteCompile($"会话结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteCompile("===================\n");

        WriteDebug($"会话结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteDebug("===================\n");
    }

    /// <summary>
    /// 自定义 TraceListener，用于捕获 Debug.WriteLine 输出
    /// </summary>
    private class DebugTraceListener : TraceListener
    {
        private readonly LogManager _logManager;

        public DebugTraceListener(LogManager logManager)
        {
            _logManager = logManager;
        }

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _logManager.WriteDebug(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _logManager.WriteDebug(message);
            }
        }
    }
}
