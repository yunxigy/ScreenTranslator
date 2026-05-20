using System.IO;

namespace ScreenTranslator.Core;

/// <summary>
/// 简单的文件日志记录器
/// 日志文件: %APPDATA%/ScreenTranslator/app.log
/// </summary>
internal static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenTranslator");

    private static readonly string LogFile = Path.Combine(LogDir, "app.log");
    private static readonly object _lock = new();

    static Logger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);
        }
        catch { }
    }

    internal static void Info(string message) => Write("INFO", message);
    internal static void Warn(string message) => Write("WARN", message);
    internal static void Error(string message) => Write("ERROR", message);
    internal static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                File.AppendAllText(LogFile, line);
            }
            System.Diagnostics.Debug.Write(line);
        }
        catch { }
    }

    internal static string GetLogFilePath() => LogFile;

    internal static void Clear()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogFile))
                    File.WriteAllText(LogFile, string.Empty);
            }
        }
        catch { }
    }
}
