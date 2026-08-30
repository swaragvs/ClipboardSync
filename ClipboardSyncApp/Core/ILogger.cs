namespace ClipboardSyncApp.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void LogInfo(string message) => Log(LogLevel.Info, message);
    void LogWarn(string message) => Log(LogLevel.Warn, message);
    void LogError(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
    void LogDebug(string message) => Log(LogLevel.Debug, message);
}

public sealed class ConsoleLogger : ILogger
{
    public event EventHandler<string>? LogEmitted;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}" + (exception != null ? $" Exception: {exception.Message}" : string.Empty);
        LogEmitted?.Invoke(this, formatted);
    }
}
