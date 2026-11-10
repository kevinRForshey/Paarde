using System.Text;

using Microsoft.Extensions.Logging;

namespace Paarde.Avalonia.Services;

// A user-accessible plain-text log file under <ApplicationDataFolder>/Logs (App.axaml.cs
// resolves the path and registers this via ConfigureLogging, the same way Host.CreateDefaultBuilder
// already wires up its own Console/Debug providers) - so a user hitting a bug can send a file
// instead of a screen recording. No external logging package (Serilog/NLog) was pulled in for this:
// Microsoft.Extensions.Logging.Abstractions is already a transitive dependency of Hosting, and
// "append one formatted line per log call" doesn't need more than that.
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        // Each write opens and closes the file itself (see WriteLine) rather than holding a handle
        // open for the provider's lifetime, so there's nothing to release here.
    }

    private void WriteLine(string line)
    {
        lock (_writeLock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelLabel(logLevel)}] {_categoryName}: {formatter(state, exception)}";

            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.WriteLine(line);
        }

        private static string LevelLabel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => logLevel.ToString(),
        };
    }
}
