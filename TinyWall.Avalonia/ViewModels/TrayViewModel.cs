using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace pylorak.TinyWall
{
    public partial class TrayViewModel : ObservableObject
    {
        private readonly Controller _controller;

        /// <summary>
        /// Callback that shows a password dialog and returns the password hash,
        /// or null if cancelled. Set by the App on startup.
        /// </summary>
        public Func<Task<string?>>? ShowPasswordDialog { get; set; }

        [ObservableProperty]
        private FirewallMode _currentMode = FirewallMode.Unknown;

        [ObservableProperty]
        private bool _isLocked;

        [ObservableProperty]
        private string _trafficRateText = "--";

        [ObservableProperty]
        private bool _isLocalSubnetAllowed;

        [ObservableProperty]
        private bool _isHostsBlocklistEnabled;

        public event EventHandler? QuitRequested;

        public TrayViewModel(Controller controller)
        {
            _controller = controller;
        }

        [RelayCommand]
        private void SetMode(FirewallMode mode)
        {
            try
            {
                var resp = _controller.SwitchFirewallMode(mode);
                switch (resp)
                {
                    case MessageType.MODE_SWITCH:
                        CurrentMode = mode;
                        string msg = mode switch
                        {
                            FirewallMode.Normal => Resources.Messages.TheFirewallIsNowOperatingAsRecommended,
                            FirewallMode.AllowOutgoing => Resources.Messages.TheFirewallIsNowAllowsOutgoingConnections,
                            FirewallMode.BlockAll => Resources.Messages.TheFirewallIsNowBlockingAllInAndOut,
                            FirewallMode.Disabled => Resources.Messages.TheFirewallIsNowDisabled,
                            FirewallMode.Learning => Resources.Messages.TheFirewallIsNowLearning,
                            _ => string.Empty
                        };
                        if (!string.IsNullOrEmpty(msg))
                            NotificationService.Notify(msg);
                        break;
                    case MessageType.RESPONSE_LOCKED:
                        NotificationService.Notify(Resources.Messages.TinyWallIsCurrentlyLocked, NotificationLevel.Warning);
                        break;
                    case MessageType.COM_ERROR:
                        NotificationService.Notify(Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
                        break;
                    default:
                        NotificationService.Notify(Resources.Messages.OperationFailed, NotificationLevel.Error);
                        break;
                }
            }
            catch
            {
                NotificationService.Notify(Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
        }

        [RelayCommand]
        private void OpenManage()
        {
            // TODO: Open Manage window in a future phase
        }

        [RelayCommand]
        private void OpenConnections()
        {
            // TODO: Open Connections window in a future phase
        }

        [RelayCommand]
        private void WhitelistByExecutable()
        {
            // TODO: Implement whitelist-by-executable in a future phase
        }

        [RelayCommand]
        private void WhitelistByProcess()
        {
            // TODO: Implement whitelist-by-process in a future phase
        }

        [RelayCommand]
        private void WhitelistByWindow()
        {
            // TODO: Implement whitelist-by-window in a future phase
        }

        [RelayCommand]
        private void ToggleLocalSubnet()
        {
            try
            {
                Guid changeset = System.Guid.Empty;
                _controller.GetServerConfig(out var config, out _, ref changeset);
                if (config == null) return;

                config.ActiveProfile.AllowLocalSubnet = !config.ActiveProfile.AllowLocalSubnet;
                var resp = _controller.SetServerConfig(config, changeset);
                if (resp.Type == MessageType.PUT_SETTINGS)
                {
                    IsLocalSubnetAllowed = config.ActiveProfile.AllowLocalSubnet;
                    NotificationService.Notify(pylorak.TinyWall.Resources.Messages.TheFirewallSettingsHaveBeenUpdated);
                }
            }
            catch
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
        }

        [RelayCommand]
        private void ToggleHostsBlocklist()
        {
            try
            {
                Guid changeset = System.Guid.Empty;
                _controller.GetServerConfig(out var config, out _, ref changeset);
                if (config == null) return;

                config.Blocklists.EnableHostsBlocklist = !config.Blocklists.EnableHostsBlocklist;
                var resp = _controller.SetServerConfig(config, changeset);
                if (resp.Type == MessageType.PUT_SETTINGS)
                {
                    IsHostsBlocklistEnabled = config.Blocklists.EnableHostsBlocklist;
                    NotificationService.Notify(pylorak.TinyWall.Resources.Messages.TheFirewallSettingsHaveBeenUpdated);
                }
            }
            catch
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
        }

        [RelayCommand]
        internal async Task ToggleLockAsync()
        {
            try
            {
                if (IsLocked)
                {
                    if (ShowPasswordDialog == null)
                        return;

                    string? passHash = await ShowPasswordDialog();
                    if (passHash == null)
                        return; // cancelled

                    var result = _controller.TryUnlockServer(passHash);
                    if (result == MessageType.UNLOCK)
                    {
                        IsLocked = false;
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.TinyWallHasBeenUnlocked);
                    }
                    else
                    {
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.UnlockFailed, NotificationLevel.Error);
                    }
                }
                else
                {
                    _controller.LockServer();
                    IsLocked = true;
                }
            }
            catch
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
        }

        [RelayCommand]
        private void Quit()
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
