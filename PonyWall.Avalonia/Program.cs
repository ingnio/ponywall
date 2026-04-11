using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using Avalonia;
using Microsoft.Extensions.Logging;
using pylorak.TinyWall.Logging;
using pylorak.Windows.Services;

namespace pylorak.TinyWall
{
    static class Program
    {
        private static Mutex? _singleInstanceMutex;

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

            // Point the service-registration path at the sibling PonyWallService.exe.
            // Must happen before either the /install branch or the normal-launch
            // branch — both of them read Utils.ServiceExecutablePath.
            string serviceExePath = Path.Combine(
                Path.GetDirectoryName(Utils.ExecutablePath)!,
                "PonyWallService.exe");
            if (File.Exists(serviceExePath))
                Utils.ServiceExecutablePath = serviceExePath;

            // /install mode: this process was spawned by a non-elevated parent via
            // UAC in order to install the Windows service. We deliberately skip
            // the single-instance mutex (the parent still holds it, and it's
            // Local-scoped so the elevated child sees it too) and we do NOT start
            // the UI — just install the service and exit so the parent's
            // p.WaitForExit() can return and proceed to launch the UI.
            if (Array.Exists(args, a => string.Equals(a, "/install", StringComparison.OrdinalIgnoreCase)))
            {
                return InstallServiceAsAdmin() ? 0 : 1;
            }

            // Single-instance check
            _singleInstanceMutex = new Mutex(true, @"Local\PonyWallController", out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running
                return 0;
            }

            try
            {
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
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            // Service not running — try to install and start it
            if (Utils.RunningAsAdmin())
            {
                InstallServiceAsAdmin();
            }
            else
            {
                // Not admin — re-launch self with /install to trigger UAC. The
                // elevated child hits the /install branch at the top of Main,
                // which skips the single-instance mutex and calls
                // InstallServiceAsAdmin directly. See Main for the contract.
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

        /// <summary>
        /// Installs and starts the PonyWall service. Caller MUST already be
        /// running elevated — this method does not check or prompt for UAC.
        /// Invoked from two places:
        ///   (1) The normal launch path's EnsureServiceRunning, when the UI
        ///       itself was launched as admin.
        ///   (2) Main's /install branch, which handles the elevated child
        ///       process spawned by a non-elevated parent via UAC.
        /// Returns true on success, false if either the install or start failed
        /// (exceptions are caught and logged in both cases).
        /// </summary>
        private static bool InstallServiceAsAdmin()
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
                return false;
            }

            try
            {
                using var sc = new ServiceController(TinyWallService.SERVICE_NAME);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                }
                return true;
            }
            catch (Exception e)
            {
                Utils.LogException(e, Utils.LOG_ID_GUI);
                return false;
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
