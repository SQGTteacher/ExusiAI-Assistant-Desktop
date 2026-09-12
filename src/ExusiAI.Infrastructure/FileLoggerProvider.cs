using Microsoft.Extensions.Logging;

namespace ExusiAI.Infrastructure;

public sealed class FileLoggerProvider(IAppPaths paths) : ILoggerProvider
{
    private readonly object sync = new();
    private bool disposed;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => disposed = true;

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var line = $"{DateTimeOffset.Now:O} [{level}] {category}: {message}";
            if (exception is not null)
            {
                line += $" {exception.GetType().Name}: {exception.Message}";
            }

            lock (sync)
            {
                File.AppendAllText(Path.Combine(paths.LogsDirectory, "exusiai.log"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take down the host.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
