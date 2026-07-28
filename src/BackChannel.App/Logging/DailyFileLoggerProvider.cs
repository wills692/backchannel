using Microsoft.Extensions.Logging;

namespace BackChannel.App.Logging;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly Lock _writeLock = new();

    private bool _disposed;

    public DailyFileLoggerProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DailyFileLogger(this, categoryName);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (_disposed || level == LogLevel.None)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var exceptionText = exception is null
            ? string.Empty
            : $"{Environment.NewLine}{exception}";
        var line =
            $"{now:O} [{level}] {category} ({eventId.Id}): {message}{exceptionText}{Environment.NewLine}";

        lock (_writeLock)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(
                    _directory,
                    $"backchannel-{now:yyyyMMdd}.log");
                File.AppendAllText(path, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class DailyFileLogger(
        DailyFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (IsEnabled(logLevel))
            {
                provider.Write(
                    logLevel,
                    category,
                    eventId,
                    formatter(state, exception),
                    exception);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
