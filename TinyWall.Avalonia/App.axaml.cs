using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

        // Menu items that need dynamic updates
        private NativeMenuItem? _mnuTrafficRate;
        private NativeMenuItem? _mnuModeNormal;
        private NativeMenuItem? _mnuModeBlockAll;
        private NativeMenuItem? _mnuModeAllowOutgoing;
        private NativeMenuItem? _mnuModeDisabled;
        private NativeMenuItem? _mnuModeLearn;
        private NativeMenuItem? _mnuAllowLocalSubnet;
        private NativeMenuItem? _mnuEnableHostsBlocklist;
        private NativeMenuItem? _mnuLock;

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
            var menu = new NativeMenu();

            // Traffic rate display (informational, disabled)
            _mnuTrafficRate = new NativeMenuItem("Traffic: --") { IsEnabled = false };
            menu.Items.Add(_mnuTrafficRate);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Mode submenu
            var modeMenu = new NativeMenu();
            _mnuModeNormal = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.FirewallModeNormal);
            _mnuModeNormal.Click += (_, _) => _viewModel?.SetModeCommand.Execute(FirewallMode.Normal);
            modeMenu.Items.Add(_mnuModeNormal);

            _mnuModeBlockAll = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.FirewallModeBlockAll);
            _mnuModeBlockAll.Click += (_, _) => _viewModel?.SetModeCommand.Execute(FirewallMode.BlockAll);
            modeMenu.Items.Add(_mnuModeBlockAll);

            _mnuModeAllowOutgoing = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.FirewallModeAllowOut);
            _mnuModeAllowOutgoing.Click += (_, _) => _viewModel?.SetModeCommand.Execute(FirewallMode.AllowOutgoing);
            modeMenu.Items.Add(_mnuModeAllowOutgoing);

            _mnuModeDisabled = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.FirewallModeDisabled);
            _mnuModeDisabled.Click += (_, _) => _viewModel?.SetModeCommand.Execute(FirewallMode.Disabled);
            modeMenu.Items.Add(_mnuModeDisabled);

            _mnuModeLearn = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.FirewallModeLearn);
            _mnuModeLearn.Click += (_, _) => _viewModel?.SetModeCommand.Execute(FirewallMode.Learning);
            modeMenu.Items.Add(_mnuModeLearn);

            var mnuMode = new NativeMenuItem("Change mode") { Menu = modeMenu };
            menu.Items.Add(mnuMode);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Manage
            var mnuManage = new NativeMenuItem("Manage");
            mnuManage.Click += async (_, _) => await OpenManageAsync();
            menu.Items.Add(mnuManage);

            // Connections
            var mnuConnections = new NativeMenuItem("Connections...");
            mnuConnections.Click += (_, _) => OpenConnections();
            menu.Items.Add(mnuConnections);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Whitelist by submenu
            var whitelistMenu = new NativeMenu();
            var mnuWhitelistExe = new NativeMenuItem("Executable...");
            mnuWhitelistExe.Click += async (_, _) => await WhitelistByExecutableAsync();
            whitelistMenu.Items.Add(mnuWhitelistExe);

            var mnuWhitelistProc = new NativeMenuItem("Process...");
            mnuWhitelistProc.Click += async (_, _) => await WhitelistByProcessAsync();
            whitelistMenu.Items.Add(mnuWhitelistProc);

            var mnuWhitelistWin = new NativeMenuItem("Window...");
            mnuWhitelistWin.Click += (_, _) => ToggleWhitelistByWindow();
            whitelistMenu.Items.Add(mnuWhitelistWin);

            var mnuWhitelist = new NativeMenuItem("Whitelist by") { Menu = whitelistMenu };
            menu.Items.Add(mnuWhitelist);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Allow local subnet toggle
            _mnuAllowLocalSubnet = new NativeMenuItem("Allow Local Subnet");
            _mnuAllowLocalSubnet.Click += (_, _) => _viewModel?.ToggleLocalSubnetCommand.Execute(null);
            menu.Items.Add(_mnuAllowLocalSubnet);

            // Enable hosts blocklist toggle
            _mnuEnableHostsBlocklist = new NativeMenuItem("Enable Hosts Blocklist");
            _mnuEnableHostsBlocklist.Click += (_, _) => _viewModel?.ToggleHostsBlocklistCommand.Execute(null);
            menu.Items.Add(_mnuEnableHostsBlocklist);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Lock
            _mnuLock = new NativeMenuItem(pylorak.TinyWall.Resources.Messages.Lock);
            _mnuLock.Click += async (_, _) =>
            {
                if (_viewModel != null)
                    await _viewModel.ToggleLockAsync();
            };
            menu.Items.Add(_mnuLock);
            menu.Items.Add(new NativeMenuItemSeparator());

            // Quit
            var mnuQuit = new NativeMenuItem("Quit");
            mnuQuit.Click += (_, _) => _viewModel?.QuitCommand.Execute(null);
            menu.Items.Add(mnuQuit);

            // Create the tray icon
            var trayIcons = new TrayIcons();
            _trayIcon = new TrayIcon
            {
                ToolTipText = "TinyWall",
                Menu = menu
            };

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

                // Update menu check marks based on current mode
                UpdateModeMenuChecks();
                UpdateToggleMenuText();
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

                if (_mnuTrafficRate != null)
                    _mnuTrafficRate.Header = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                        "{0}: {1}    {2}: {3}",
                        pylorak.TinyWall.Resources.Messages.TrafficIn, rxDisplay,
                        pylorak.TinyWall.Resources.Messages.TrafficOut, txDisplay);
            }
            catch { }
        }

        private void UpdateModeMenuChecks()
        {
            if (_viewModel == null) return;

            // Avalonia NativeMenuItem doesn't support checkmarks directly,
            // so we use a bullet prefix to indicate the active mode.
            var mode = _viewModel.CurrentMode;
            SetMenuItemChecked(_mnuModeNormal, mode == FirewallMode.Normal);
            SetMenuItemChecked(_mnuModeBlockAll, mode == FirewallMode.BlockAll);
            SetMenuItemChecked(_mnuModeAllowOutgoing, mode == FirewallMode.AllowOutgoing);
            SetMenuItemChecked(_mnuModeDisabled, mode == FirewallMode.Disabled);
            SetMenuItemChecked(_mnuModeLearn, mode == FirewallMode.Learning);
        }

        private static void SetMenuItemChecked(NativeMenuItem? item, bool isChecked)
        {
            if (item == null) return;

            // Use a toggleType to show check state
            item.ToggleType = NativeMenuItemToggleType.Radio;
            item.IsChecked = isChecked;
        }

        private void UpdateToggleMenuText()
        {
            if (_viewModel == null) return;

            if (_mnuAllowLocalSubnet != null)
            {
                _mnuAllowLocalSubnet.ToggleType = NativeMenuItemToggleType.CheckBox;
                _mnuAllowLocalSubnet.IsChecked = _viewModel.IsLocalSubnetAllowed;
            }

            if (_mnuEnableHostsBlocklist != null)
            {
                _mnuEnableHostsBlocklist.ToggleType = NativeMenuItemToggleType.CheckBox;
                _mnuEnableHostsBlocklist.IsChecked = _viewModel.IsHostsBlocklistEnabled;
            }

            if (_mnuLock != null)
            {
                _mnuLock.Header = _viewModel.IsLocked ? pylorak.TinyWall.Resources.Messages.Unlock : pylorak.TinyWall.Resources.Messages.Lock;
            }
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
