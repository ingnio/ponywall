using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Central logging facade. Each entry point (UI exe, service exe) calls
    /// Configure() once on startup with an ILoggerFactory. All other code
    /// uses TinyWallLog.Logger(category) to get an ILogger instance.
    ///
    /// Until Configure() is called, all loggers are no-op (NullLogger).
    /// </summary>
    public static class TinyWallLog
    {
        private static ILoggerFactory _factory = NullLoggerFactory.Instance;

        public static void Configure(ILoggerFactory factory)
        {
            _factory = factory ?? NullLoggerFactory.Instance;
        }

        public static ILogger Logger(string category) => _factory.CreateLogger(category);

        public static ILogger<T> Logger<T>() => _factory.CreateLogger<T>();
    }
}
