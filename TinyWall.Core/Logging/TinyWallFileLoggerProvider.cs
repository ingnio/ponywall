using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace pylorak.TinyWall.Logging
{
    /// <summary>
    /// Logger provider that writes to %ProgramData%\PonyWall\logs\{category}.log,
    /// preserving the legacy Utils.Log file format (blank line, dashed header,
    /// message body, blank line) so that existing log readers continue to work.
    ///
    /// Files are truncated to 0 bytes when they exceed 512 KB. All I/O errors
    /// are swallowed silently — logging must never throw.
    /// </summary>
    public sealed class TinyWallFileLoggerProvider : ILoggerProvider
    {
        private const long MaxFileSizeBytes = 512 * 1024;

        private static readonly object FileLock = new();
        private readonly ConcurrentDictionary<string, TinyWallFileLogger> _loggers = new();

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new TinyWallFileLogger(name));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }

        internal static void WriteEntry(string category, string info)
        {
            try
            {
                lock (FileLock)
                {
                    string logdir = Path.Combine(Utils.AppDataPath, "logs");
                    string logfile = Path.Combine(logdir, $"{category}.log");

                    if (!Directory.Exists(logdir))
                        Directory.CreateDirectory(logdir);

                    if (File.Exists(logfile))
                    {
                        var fi = new FileInfo(logfile);
                        if (fi.Length > MaxFileSizeBytes)
                        {
                            using var fs = new FileStream(logfile, FileMode.Truncate, FileAccess.Write);
                        }
                    }

                    using var sw = new StreamWriter(logfile, true, Encoding.UTF8);
                    sw.WriteLine();
                    sw.WriteLine("------- " + DateTime.Now.ToString(CultureInfo.InvariantCulture) + " -------");
                    sw.WriteLine(info);
                    sw.WriteLine();
                }
            }
            catch
            {
                // Ignore exceptions - logging should not itself cause new problems
            }
        }

        private sealed class TinyWallFileLogger : ILogger
        {
            private readonly string _category;

            public TinyWallFileLogger(string category)
            {
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;
                if (formatter is null)
                    return;

                string message = formatter(state, exception);

                if (exception is not null)
                {
                    // Include the exception's full stack trace. The caller
                    // (e.g. Utils.LogException) is responsible for including any
                    // version/environment context in the message itself.
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(message))
                    {
                        sb.AppendLine(message);
                    }
                    sb.Append(exception.ToString());
                    message = sb.ToString();
                }
                else if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                WriteEntry(_category, message);
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                private NullScope() { }
                public void Dispose() { }
            }
        }
    }
}
