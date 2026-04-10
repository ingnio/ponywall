using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using Avalonia;
using pylorak.Windows.Services;

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

                // Ensure the service is installed and running
                EnsureServiceRunning();

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
                return 0;
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }

        private static void EnsureServiceRunning()
        {
            try
            {
                using var sc = new ServiceController(TinyWallService.SERVICE_NAME);
                if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                    return;
            }
            catch { }

            // Service not running — try to install and start it
            if (Utils.RunningAsAdmin())
            {
                try
                {
                    using var scm = new ServiceControlManager(
                        ServiceControlAccessRights.SC_MANAGER_CONNECT |
                        ServiceControlAccessRights.SC_MANAGER_CREATE_SERVICE);
                    scm.InstallService(
                        TinyWallService.SERVICE_NAME,
                        TinyWallService.SERVICE_DISPLAY_NAME,
                        Utils.ServiceExecutablePath,
                        TinyWallService.ServiceDependencies,
                        ServiceStartMode.Automatic);
                    scm.SetLoadOrderGroup(TinyWallService.SERVICE_NAME, "NetworkProvider");
                }
                catch (Exception e)
                {
                    Utils.LogException(e, Utils.LOG_ID_GUI);
                }

                try
                {
                    using var sc = new ServiceController(TinyWallService.SERVICE_NAME);
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception e)
                {
                    Utils.LogException(e, Utils.LOG_ID_GUI);
                }
            }
            else
            {
                // Not admin — re-launch self with /install to trigger UAC
                try
                {
                    using Process p = Utils.StartProcess(Utils.ExecutablePath, "/install", true);
                    p.WaitForExit();
                }
                catch (Exception e)
                {
                    Utils.LogException(e, Utils.LOG_ID_GUI);
                }
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
