using System;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using pylorak.TinyWall.Logging;
using pylorak.Utilities;

namespace pylorak.TinyWall
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Configure logging as early as possible so that everything else
            // on the startup path routes through ILogger.
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new TinyWallFileLoggerProvider());
#if DEBUG
                builder.AddDebug();
#endif
                builder.SetMinimumLevel(LogLevel.Information);
            });
            TinyWallLog.Configure(loggerFactory);

            HierarchicalStopwatch.Enable = File.Exists(Path.Combine(Utils.AppDataPath, "enable-timings"));
            HierarchicalStopwatch.LogFileBase = Path.Combine(Utils.AppDataPath, @"logs\timings");

            try
            {
                Utils.SafeNativeMethods.WerAddExcludedApplication(Utils.ExecutablePath, true);
            }
            catch { }

            // Setup TLS 1.2 & 1.3 support
            if (ServicePointManager.SecurityProtocol != 0)
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls13; } catch { }
            }

#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Utils.LogException((Exception)e.ExceptionObject, Utils.LOG_ID_SERVICE);
            };
#endif

            using var srv = new TinyWallService();

#if DEBUG
            if (!Utils.RunningAsAdmin())
            {
                Console.WriteLine("Error: Not started as an admin process.");
                return -1;
            }
#endif

            using var singleInstanceMutex = new Mutex(true, @"Global\TinyWallService", out bool mutexOk);
            if (!mutexOk)
                return -1;

#if DEBUG
            srv.Start(Array.Empty<string>());
            srv.StartedEvent.WaitOne();
            Console.WriteLine("Kill process to terminate...");
            srv.StoppedEvent.WaitOne();
#else
            pylorak.Windows.PathMapper.Instance.AutoUpdate = false;
            pylorak.Windows.Services.ServiceBase.Run(srv);
#endif
            return 0;
        }
    }
}
