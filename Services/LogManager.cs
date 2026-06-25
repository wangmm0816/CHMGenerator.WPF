using System.IO;
using System.Text;

namespace CHMGenerator.WPF.Services;

/// <summary>
/// 日志管理服务
/// 自动将操作日志和编译日志保存到 logs 目录
/// </summary>
public class LogManager
{
    private static LogManager? _instance;
    private readonly string _logDirectory;
    private string? _currentOperationLogPath;
    private string? _currentCompileLogPath;
    private readonly object _lockObject = new();

    private LogManager()
    {
        // 在项目目录下创建 logs 文件夹
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public static LogManager Instance => _instance ??= new LogManager();

    /// <summary>
    /// 开始新的会话，创建新的日志文件
    /// </summary>
    public void StartNewSession()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentOperationLogPath = Path.Combine(_logDirectory, $"operation_{timestamp}.txt");
        _currentCompileLogPath = Path.Combine(_logDirectory, $"compile_{timestamp}.txt");

        // 创建日志文件并写入头部信息
        var header = $"=== CHM Generator 日志 ===\n会话开始时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";

        lock (_lockObject)
        {
            File.WriteAllText(_currentOperationLogPath, header, Encoding.UTF8);
            File.WriteAllText(_currentCompileLogPath, header, Encoding.UTF8);
        }

        WriteOperation($"日志文件已创建: {Path.GetFileName(_currentOperationLogPath)}");
    }

    /// <summary>
    /// 写入操作日志
    /// </summary>
    public void WriteOperation(string message)
    {
        if (string.IsNullOrEmpty(_currentOperationLogPath)) StartNewSession();

        var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_currentOperationLogPath!, logEntry, Encoding.UTF8);
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
    /// 写入状态变更日志
    /// </summary>
    public void WriteStatus(string statusText)
    {
        WriteOperation($"状态: {statusText}");
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

        WriteOperation(exceptionLog.ToString());
    }

    /// <summary>
    /// 获取当前操作日志文件路径
    /// </summary>
    public string? GetCurrentOperationLogPath() => _currentOperationLogPath;

    /// <summary>
    /// 获取当前编译日志文件路径
    /// </summary>
    public string? GetCurrentCompileLogPath() => _currentCompileLogPath;

    /// <summary>
    /// 获取日志目录路径
    /// </summary>
    public string GetLogDirectory() => _logDirectory;

    /// <summary>
    /// 清理旧日志文件（保留最近 30 天）
    /// </summary>
    public void CleanOldLogs(int keepDays = 30)
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-keepDays);
            var logFiles = Directory.GetFiles(_logDirectory, "*.txt");

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoffDate)
                {
                    try
                    {
                        File.Delete(file);
                        WriteOperation($"已删除旧日志: {Path.GetFileName(file)}");
                    }
                    catch
                    {
                        // 删除失败，忽略
                    }
                }
            }
        }
        catch
        {
            // 清理失败，忽略
        }
    }

    /// <summary>
    /// 打开日志目录
    /// </summary>
    public void OpenLogDirectory()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _logDirectory);
        }
        catch (Exception ex)
        {
            WriteOperation($"打开日志目录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 结束当前会话
    /// </summary>
    public void EndSession()
    {
        WriteOperation($"会话结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteOperation("===================\n");

        WriteCompile($"会话结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteCompile("===================\n");
    }
}
