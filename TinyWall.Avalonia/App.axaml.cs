using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using pylorak.TinyWall.Views;

namespace pylorak.TinyWall
{
    public partial class App : Application
    {
        private Controller? _controller;
        private Guid _clientChangeset = Guid.Empty;
        private ServerState? _lastServerState;
        private ServerConfiguration? _lastServerConfig;
        private TrayIcon? _trayIcon;
        private DispatcherTimer? _pollTimer;
        private TrayViewModel? _viewModel;
        private readonly pylorak.Windows.TrafficRateMonitor _trafficMonitor = new();
        private readonly pylorak.Windows.MouseInterceptor _mouseInterceptor = new();
        private bool _whitelistByWindowActive;
        private string _trafficRateText = "Traffic: --";
        private ThemeVariant _currentThemeVariant = ThemeVariant.Default;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // No main window -- this is a tray-only app
                desktop.MainWindow = null;

                // Initialize the Controller (pipe client to the TinyWall service)
                _controller = new Controller("TinyWallController");

                // Load the application database (used by Settings, AppFinder, etc.)
                try
                {
                    ServiceGlobals.AppDatabase = DatabaseClasses.AppDatabase.Load();
                }
                catch
                {
                    ServiceGlobals.AppDatabase = new DatabaseClasses.AppDatabase();
                }

                // Create the view model
                _viewModel = new TrayViewModel(_controller);
                _viewModel.QuitRequested += (_, _) =>
                {
                    _pollTimer?.Stop();
                    _mouseInterceptor.Dispose();
                    _trafficMonitor.Dispose();
                    NotificationService.Cleanup();
                    desktop.Shutdown();
                };

                // Wire password dialog callback
                _viewModel.ShowPasswordDialog = ShowPasswordDialogAsync;

                // Build the tray icon
                SetupTrayIcon();

                // Start polling the service for state updates
                _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _pollTimer.Tick += OnPollTimer;
                _pollTimer.Start();

                // Do an initial poll immediately
                OnPollTimer(null, EventArgs.Empty);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void SetupTrayIcon()
        {
            // Create a tray icon with no NativeMenu -- we show a custom popup window instead
            var trayIcons = new TrayIcons();
            _trayIcon = new TrayIcon
            {
                ToolTipText = "TinyWall"
            };

            // Left-click: show custom styled popup menu
            _trayIcon.Clicked += (_, _) => ShowTrayMenu();

            // Right-click: Avalonia shows NativeMenu automatically.
            // Rebuild it fresh each time so it reflects current state.
            _trayIcon.Menu = BuildNativeMenu();
            // Refresh before each show
            if (_trayIcon.Menu is NativeMenu nm)
            {
                nm.Opening += (_, _) =>
                {
                    var fresh = BuildNativeMenu();
                    nm.Items.Clear();
                    // Move items one by one (adding to nm removes from fresh)
                    while (fresh.Items.Count > 0)
                    {
                        var item = fresh.Items[0];
                        fresh.Items.RemoveAt(0);
                        nm.Items.Add(item);
                    }
                };
            }

            // Load the tray icon from embedded Avalonia resource
            try
            {
                var assets = Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://TinyWall.Avalonia/Assets/firewall.ico"));
                _trayIcon.Icon = new WindowIcon(assets);
            }
            catch
            {
                // Fallback: try loading from file next to executable
                try
                {
                    var iconPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(Utils.ExecutablePath)!, "firewall.ico");
                    if (System.IO.File.Exists(iconPath))
                        _trayIcon.Icon = new WindowIcon(iconPath);
                }
                catch { }
            }

            trayIcons.Add(_trayIcon);
            TrayIcon.SetIcons(this, trayIcons);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private NativeMenu BuildNativeMenu()
        {
            var menu = new NativeMenu();

            // Mode submenu
            var modeMenu = new NativeMenu();
            var modes = new[] {
                (FirewallMode.Normal, pylorak.TinyWall.Resources.Messages.FirewallModeNormal),
                (FirewallMode.BlockAll, pylorak.TinyWall.Resources.Messages.FirewallModeBlockAll),
                (FirewallMode.AllowOutgoing, pylorak.TinyWall.Resources.Messages.FirewallModeAllowOut),
                (FirewallMode.Disabled, pylorak.TinyWall.Resources.Messages.FirewallModeDisabled),
                (FirewallMode.Learning, pylorak.TinyWall.Resources.Messages.FirewallModeLearn),
            };
            foreach (var (mode, label) in modes)
            {
                var item = new NativeMenuItem(label);
                item.ToggleType = NativeMenuItemToggleType.Radio;
                item.IsChecked = _viewModel?.CurrentMode == mode;
                var m = mode;
                item.Click += (_, _) => _viewModel?.SetModeCommand.Execute(m);
                modeMenu.Items.Add(item);
            }
            menu.Items.Add(new NativeMenuItem("Change mode") { Menu = modeMenu });
            menu.Items.Add(new NativeMenuItemSeparator());

            var mnuManage = new NativeMenuItem("Manage");
            mnuManage.Click += async (_, _) => await OpenManageAsync();
            menu.Items.Add(mnuManage);

            var mnuConn = new NativeMenuItem("Connections...");
            mnuConn.Click += (_, _) => OpenConnections();
            menu.Items.Add(mnuConn);
            menu.Items.Add(new NativeMenuItemSeparator());

            var mnuLock = new NativeMenuItem(_viewModel?.IsLocked == true
                ? pylorak.TinyWall.Resources.Messages.Unlock
                : pylorak.TinyWall.Resources.Messages.Lock);
            mnuLock.Click += async (_, _) => { if (_viewModel != null) await _viewModel.ToggleLockAsync(); };
            menu.Items.Add(mnuLock);
            menu.Items.Add(new NativeMenuItemSeparator());

            var mnuQuit = new NativeMenuItem("Quit");
            mnuQuit.Click += (_, _) => _viewModel?.QuitCommand.Execute(null);
            menu.Items.Add(mnuQuit);

            return menu;
        }

        private void ShowTrayMenu()
        {
            if (_viewModel == null) return;

            // Get current cursor position for menu placement
            GetCursorPos(out var pt);
            var cursorPos = new PixelPoint(pt.X, pt.Y);

            var menuWindow = new TrayMenuWindow();

            // Set current state
            menuWindow.SetState(
                _viewModel.CurrentMode,
                _viewModel.IsLocked,
                _viewModel.IsLocalSubnetAllowed,
                _viewModel.IsHostsBlocklistEnabled,
                _currentThemeVariant,
                _trafficRateText);

            // Wire up events
            menuWindow.ModeChangeRequested += mode => _viewModel.SetModeCommand.Execute(mode);
            menuWindow.ManageRequested += async () => await OpenManageAsync();
            menuWindow.ConnectionsRequested += () => OpenConnections();
            menuWindow.WhitelistExeRequested += async () => await WhitelistByExecutableAsync();
            menuWindow.WhitelistProcessRequested += async () => await WhitelistByProcessAsync();
            menuWindow.WhitelistWindowRequested += () => ToggleWhitelistByWindow();
            menuWindow.ToggleLocalSubnetRequested += () => _viewModel.ToggleLocalSubnetCommand.Execute(null);
            menuWindow.ToggleHostsBlocklistRequested += () => _viewModel.ToggleHostsBlocklistCommand.Execute(null);
            menuWindow.ToggleLockRequested += async () =>
            {
                if (_viewModel != null)
                    await _viewModel.ToggleLockAsync();
            };
            menuWindow.ThemeChangeRequested += variant => SetThemeVariant(variant);
            menuWindow.QuitRequested += () => _viewModel.QuitCommand.Execute(null);

            menuWindow.ShowAt(cursorPos);
        }

        private async Task OpenManageAsync()
        {
            if (_controller == null) return;

            try
            {
                // Force a full config load by using an empty changeset
                Guid forceLoad = Guid.Empty;
                var respType = _controller.GetServerConfig(out var config, out _, ref forceLoad);
                if (config == null || respType != MessageType.GET_SETTINGS)
                {
                    NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
                    return;
                }
                _clientChangeset = forceLoad;

                var result = await SettingsWindow.ShowSettingsDialog(
                    Utils.DeepClone(config),
                    new ControllerSettings());

                if (result.HasValue)
                {
                    var (tmpConfig, newPassword) = result.Value;

                    // Apply server config
                    var resp = _controller.SetServerConfig(tmpConfig.Service, _clientChangeset);
                    if (resp.Type == MessageType.PUT_SETTINGS)
                    {
                        var putResp = (TwMessagePutSettings)resp;
                        _clientChangeset = putResp.Changeset;
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.TheFirewallSettingsHaveBeenUpdated);
                    }
                    else
                    {
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CouldNotApplySettingsInternalError, NotificationLevel.Warning);
                    }

                    // Handle password change
                    if (newPassword != null)
                    {
                        string hash = string.IsNullOrEmpty(newPassword) ? string.Empty : Hasher.HashString(newPassword);
                        _controller.SetPassphrase(hash);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify($"Manage error: {ex.GetType().Name}: {ex.Message}", NotificationLevel.Error);
            }
        }

        private void OpenConnections()
        {
            if (_controller == null) return;
            ConnectionsWindow.ShowConnections(_controller);
        }

        private async Task WhitelistByExecutableAsync()
        {
            if (_controller == null) return;

            try
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // Create a temporary invisible window for the file picker
                        var tempWindow = new Window { Width = 0, Height = 0, ShowInTaskbar = false, Opacity = 0, SystemDecorations = SystemDecorations.None };
                        tempWindow.Show();

                        var topLevel = TopLevel.GetTopLevel(tempWindow);
                        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                        {
                            Title = "Select executable",
                            AllowMultiple = false,
                            FileTypeFilter = new[]
                            {
                                new Avalonia.Platform.Storage.FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                                new Avalonia.Platform.Storage.FilePickerFileType("All files") { Patterns = new[] { "*" } }
                            }
                        });

                        tempWindow.Close();

                        if (files.Count > 0)
                            tcs.TrySetResult(files[0].Path.LocalPath);
                        else
                            tcs.TrySetResult(null);
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                });

                string? filePath = await tcs.Task;
                if (filePath == null) return;

                var subject = new ExecutableSubject(filePath);
                var exceptions = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subject, false, out _)
                    ?? new System.Collections.Generic.List<FirewallExceptionV3> { new FirewallExceptionV3(subject, new TcpUdpPolicy(true)) };

                if (exceptions.Count == 0)
                    exceptions.Add(new FirewallExceptionV3(subject, new TcpUdpPolicy(true)));

                // Get current config, add exceptions, push back
                _controller.GetServerConfig(out var config, out _, ref _clientChangeset);
                if (config != null)
                {
                    config.ActiveProfile.AddExceptions(exceptions);
                    var resp = _controller.SetServerConfig(config, _clientChangeset);
                    if (resp.Type == MessageType.PUT_SETTINGS)
                    {
                        var putResp = (TwMessagePutSettings)resp;
                        _clientChangeset = putResp.Changeset;
                        NotificationService.Notify(
                            string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                pylorak.TinyWall.Resources.Messages.FirewallRulesForUnrecognizedChanged,
                                exceptions[0].Subject.ToString()));
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point pt);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static uint GetPidUnderCursor(int x, int y)
        {
            _ = GetWindowThreadProcessId(WindowFromPoint(new System.Drawing.Point(x, y)), out uint procId);
            return procId;
        }

        private void ToggleWhitelistByWindow()
        {
            if (!_whitelistByWindowActive)
            {
                _mouseInterceptor.MouseLButtonDown += OnWhitelistWindowClick;
                _mouseInterceptor.Start();
                _whitelistByWindowActive = true;
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.ClickOnAWindowWhitelisting);
            }
            else
            {
                _mouseInterceptor.Stop();
                _mouseInterceptor.MouseLButtonDown -= OnWhitelistWindowClick;
                _whitelistByWindowActive = false;
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.WhitelistingCancelled);
            }
        }

        private void OnWhitelistWindowClick(int x, int y)
        {
            // Run on a threadpool thread that marshals back to UI, so the hook
            // callback returns before we unhook (same pattern as WinForms version)
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _mouseInterceptor.Stop();
                        _mouseInterceptor.MouseLButtonDown -= OnWhitelistWindowClick;
                        _whitelistByWindowActive = false;

                        var pid = GetPidUnderCursor(x, y);
                        if (_controller == null) return;

                        var exePath = Utils.GetPathOfProcessUseTwService(pid, _controller);
                        var packageList = new UwpPackageList();
                        var appContainer = packageList.FindPackageForProcess(pid);

                        ExceptionSubject subj;
                        if (appContainer.HasValue)
                        {
                            subj = new AppContainerSubject(appContainer.Value.Sid, appContainer.Value.Name, appContainer.Value.Publisher, appContainer.Value.PublisherId);
                        }
                        else if (string.IsNullOrEmpty(exePath))
                        {
                            NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CannotGetExecutablePathWhitelisting, NotificationLevel.Error);
                            return;
                        }
                        else
                        {
                            subj = new ExecutableSubject(exePath);
                        }

                        DatabaseClasses.Application? _dummyApp;
                        var exceptions = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subj, false, out _dummyApp)
                            ?? new System.Collections.Generic.List<FirewallExceptionV3> { new FirewallExceptionV3(subj, new TcpUdpPolicy(true)) };
                        if (exceptions.Count == 0)
                            exceptions.Add(new FirewallExceptionV3(subj, new TcpUdpPolicy(true)));

                        Guid changeset = Guid.Empty;
                        ServerState? _dummyState;
                        _controller.GetServerConfig(out var config, out _dummyState, ref changeset);
                        if (config != null)
                        {
                            config.ActiveProfile.AddExceptions(exceptions);
                            var resp = _controller.SetServerConfig(config, changeset);
                            if (resp.Type == MessageType.PUT_SETTINGS)
                            {
                                var putResp = (TwMessagePutSettings)resp;
                                _clientChangeset = putResp.Changeset;
                                NotificationService.Notify(
                                    string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                        pylorak.TinyWall.Resources.Messages.FirewallRulesForUnrecognizedChanged,
                                        exceptions[0].Subject.ToString()));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogException(ex, Utils.LOG_ID_GUI);
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CannotGetExecutablePathWhitelisting, NotificationLevel.Error);
                    }
                });
            });
        }

        private async Task WhitelistByProcessAsync()
        {
            if (_controller == null) return;

            try
            {
                var selection = await ProcessesWindow.ChooseProcess(false);
                if (selection.Count == 0) return;

                var pi = selection[0];
                ExceptionSubject subject;
                if (pi.Package.HasValue)
                    subject = new AppContainerSubject(pi.Package.Value.Sid, pi.Package.Value.Name, pi.Package.Value.Publisher, pi.Package.Value.PublisherId);
                else
                    subject = new ExecutableSubject(pi.Path);

                var exceptions = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subject, false, out _)
                    ?? new System.Collections.Generic.List<FirewallExceptionV3> { new FirewallExceptionV3(subject, new TcpUdpPolicy(true)) };
                if (exceptions.Count == 0)
                    exceptions.Add(new FirewallExceptionV3(subject, new TcpUdpPolicy(true)));

                Guid changeset = Guid.Empty;
                _controller.GetServerConfig(out var config, out _, ref changeset);
                if (config != null)
                {
                    config.ActiveProfile.AddExceptions(exceptions);
                    var resp = _controller.SetServerConfig(config, changeset);
                    if (resp.Type == MessageType.PUT_SETTINGS)
                    {
                        var putResp = (TwMessagePutSettings)resp;
                        _clientChangeset = putResp.Changeset;
                        NotificationService.Notify(
                            string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                pylorak.TinyWall.Resources.Messages.FirewallRulesForUnrecognizedChanged,
                                exceptions[0].Subject.ToString()));
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify($"Error: {ex.Message}", NotificationLevel.Error);
            }
        }

        private async Task<string?> ShowPasswordDialogAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var dlg = new PasswordWindow();
                    dlg.Closed += (_, _) => tcs.TrySetResult(dlg.PassHash);
                    dlg.Show();
                    dlg.Activate();
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(null);
                    Utils.LogException(ex, Utils.LOG_ID_GUI);
                }
            });
            return await tcs.Task;
        }

        private void OnPollTimer(object? sender, EventArgs e)
        {
            if (_controller == null || _viewModel == null)
                return;

            try
            {
                var result = _controller.GetServerConfig(out var config, out var state, ref _clientChangeset);
                if (result == MessageType.GET_SETTINGS)
                {
                    if (state != null)
                    {
                        _lastServerState = state;
                        _viewModel.CurrentMode = state.Mode;
                        _viewModel.IsLocked = state.Locked;
                    }
                    if (config != null)
                    {
                        _lastServerConfig = config;
                        _viewModel.IsLocalSubnetAllowed = config.ActiveProfile.AllowLocalSubnet;
                        _viewModel.IsHostsBlocklistEnabled = config.Blocklists.EnableHostsBlocklist;
                    }
                }

                // Update traffic rate
                UpdateTrafficRate();

                // Update tray tooltip
                UpdateTrayTooltip();
            }
            catch
            {
                // Service may not be running -- silently ignore
                _viewModel.CurrentMode = FirewallMode.Unknown;
                UpdateTrayTooltip();
            }
        }

        private void UpdateTrafficRate()
        {
            try
            {
                _trafficMonitor.Update();
                float kbRx = (float)_trafficMonitor.BytesReceivedPerSec / 1024;
                float kbTx = (float)_trafficMonitor.BytesSentPerSec / 1024;
                float mbRx = kbRx / 1024;
                float mbTx = kbTx / 1024;

                string rxDisplay = (mbRx > 1)
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:f} MiB/s", mbRx)
                    : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:f} KiB/s", kbRx);
                string txDisplay = (mbTx > 1)
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:f} MiB/s", mbTx)
                    : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:f} KiB/s", kbTx);

                _trafficRateText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    "{0}: {1}    {2}: {3}",
                    pylorak.TinyWall.Resources.Messages.TrafficIn, rxDisplay,
                    pylorak.TinyWall.Resources.Messages.TrafficOut, txDisplay);
            }
            catch { }
        }

        // State is now passed to TrayMenuWindow each time it opens,
        // so no per-tick menu item updates are needed.

        private void SetThemeVariant(ThemeVariant variant)
        {
            _currentThemeVariant = variant;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = variant;
        }

        private void UpdateTrayTooltip()
        {
            if (_trayIcon == null || _viewModel == null) return;

            string modeText = _viewModel.CurrentMode switch
            {
                FirewallMode.Normal => pylorak.TinyWall.Resources.Messages.FirewallModeNormal,
                FirewallMode.BlockAll => pylorak.TinyWall.Resources.Messages.FirewallModeBlockAll,
                FirewallMode.AllowOutgoing => pylorak.TinyWall.Resources.Messages.FirewallModeAllowOut,
                FirewallMode.Disabled => pylorak.TinyWall.Resources.Messages.FirewallModeDisabled,
                FirewallMode.Learning => pylorak.TinyWall.Resources.Messages.FirewallModeLearn,
                _ => pylorak.TinyWall.Resources.Messages.FirewallModeUnknown
            };

            _trayIcon.ToolTipText = $"TinyWall - {modeText}";
        }
    }
}
