using Microsoft.Extensions.Logging.Abstractions;

namespace Youthnic.LookupApi.Logging;

/// <summary>
/// Dependency-light rolling file logger: a single lookup-api.log bounded to 10 MB,
/// rotated to .1/.2/.3 (3 files max). Companion to console logging; never logs
/// connection strings or secrets because callers never pass them as messages.
/// </summary>
public sealed class BoundedFileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private readonly string _directory;
    private readonly object _gate = new();

    public BoundedFileLoggerProvider(string? directory = null, LogLevel minLevel = LogLevel.Information)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : directory;
        MinLevel = minLevel;
    }

    public LogLevel MinLevel { get; }

    public ILogger CreateLogger(string categoryName) => new BoundedFileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, "lookup-api.log");
                RotateIfNeeded(file);
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never take the service down.
            }
        }
    }

    private void RotateIfNeeded(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        var third = Numbered(3);
        if (File.Exists(third)) File.Delete(third);
        for (var i = 2; i >= 1; i--)
        {
            var source = Numbered(i);
            if (File.Exists(source)) File.Move(source, Numbered(i + 1), overwrite: true);
        }
        File.Move(file, Numbered(1), overwrite: true);
    }

    private string Numbered(int n) => Path.Combine(_directory, $"lookup-api.{n}.log");
}

internal sealed class BoundedFileLogger(string category, BoundedFileLoggerProvider provider) : ILogger
{
    private static readonly string[] LevelTags = ["TRC", "DBG", "INF", "WRN", "ERR", "FTL", "CRI", "NONE"];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var tag = LevelTags[Math.Min((int)logLevel, LevelTags.Length - 1)];
        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {tag} {category}: {message}";
        if (exception is not null) line += $" | {exception.GetType().Name}: {exception.Message}";
        provider.WriteLine(line);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
