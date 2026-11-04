using System.IO;
using System.Text;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Tiny bounded rolling file logger. Three files of max 5 MB under
/// %LocalAppData%\Youthnic\PackingDesk\logs; when the active file exceeds
/// the cap the oldest file is dropped and the rest shift up.
/// Never log credentials: passwords are forbidden at any level; feed ids
/// may appear at Debug only.
/// </summary>
public static class AppLog
{
    public enum Level { Debug = 0, Info = 1, Error = 2 }

    private const long MaxFileBytes = 5L * 1024 * 1024;
    private const int MaxFiles = 3;

    private static readonly object Gate = new();
    private static string? _logDir;
    private static string? _currentFile;
    private static Level _minLevel = Level.Info;

    public static Level MinimumLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    private static string LogDir =>
        _logDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk", "logs");

    private static string CurrentFile =>
        _currentFile ??= Path.Combine(LogDir, "app.0.log");

    public static void Debug(string message) => Write(Level.Debug, message);

    public static void Info(string message) => Write(Level.Info, message);

    public static void Warn(string message) => Write(Level.Info, $"[WARN] {message}");

    public static void Error(string message, Exception? ex = null) =>
        Write(Level.Error, ex is null ? message : $"{message} | {ex}");

    private static void Write(Level level, string message)
    {
        if (level < _minLevel) return;

        lock (Gate)
        {
            try
            {
                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}{Environment.NewLine}";
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        Directory.CreateDirectory(LogDir);

        var info = new FileInfo(CurrentFile);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        var oldest = Path.Combine(LogDir, $"app.{MaxFiles - 1}.log");
        if (File.Exists(oldest)) File.Delete(oldest);

        for (var i = MaxFiles - 2; i >= 0; i--)
        {
            var src = Path.Combine(LogDir, $"app.{i}.log");
            if (File.Exists(src)) File.Move(src, Path.Combine(LogDir, $"app.{i + 1}.log"));
        }

        _currentFile = null; // recompute path
    }
}
