using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// Writes log lines to a file next to the console log. When the file reaches <paramref name="maxBytes"/>
/// it is moved to <c>name.1.log</c> (replacing the previous backup) and a new file is started.
/// </summary>
public sealed class FileLog(string path, long maxBytes = 1_000_000) : ILoggerProvider
{
    public static readonly string DefaultPath = Path.Combine(Path.GetDirectoryName(Config.DefaultPath)!, "matchpulse.log");

    readonly Lock _lock = new();
    StreamWriter? _writer;

    public string FilePath { get; } = path;

    /// <summary>Every logged line (time, level, message), raised on the logging thread.</summary>
    public event Action<DateTimeOffset, LogLevel, string>? Written;

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    void Write(string line)
    {
        lock (_lock)
        {
            // A log call never throws: a failed rotation keeps writing to the current file, a failed write is dropped.
            try
            {
                _writer ??= Open();
                if (_writer.BaseStream.Length >= maxBytes)
                {
                    _writer.Dispose();
                    _writer = null;
                    try { File.Move(FilePath, Path.ChangeExtension(FilePath, ".1.log"), overwrite: true); }
                    finally { _writer = Open(); }
                }
            }
            catch (Exception) { }
            try { _writer?.WriteLine(line); }
            catch (Exception) { }
        }
    }

    StreamWriter Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        return new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
    }

    public void Dispose()
    {
        lock (_lock) _writer?.Dispose();
    }

    sealed class Logger(FileLog file) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var at = DateTimeOffset.Now;
            var message = formatter(state, exception);
            var line = $"{at:yyyy-MM-dd HH:mm:ss.fff} {logLevel,-11} {message}";
            file.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
            try { file.Written?.Invoke(at, logLevel, exception is null ? message : $"{message}: {exception.Message}"); }
            catch (Exception) { }
        }
    }
}
