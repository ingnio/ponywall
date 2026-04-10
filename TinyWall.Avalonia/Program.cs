using System;
using System.IO;
using System.Threading;
using Avalonia;

namespace pylorak.TinyWall
{
    static class Program
    {
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static int Main(string[] args)
        {
            // Single-instance check
            _singleInstanceMutex = new Mutex(true, @"Local\TinyWallController", out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running
                return 0;
            }

            try
            {
                // Point service registration at the sibling TinyWallService.exe
                string serviceExePath = Path.Combine(
                    Path.GetDirectoryName(Utils.ExecutablePath)!,
                    "TinyWallService.exe");
                if (File.Exists(serviceExePath))
                    Utils.ServiceExecutablePath = serviceExePath;

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
                return 0;
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
