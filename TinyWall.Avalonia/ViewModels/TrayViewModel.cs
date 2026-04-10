using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace pylorak.TinyWall
{
    public partial class TrayViewModel : ObservableObject
    {
        private readonly Controller _controller;

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
                _controller.SwitchFirewallMode(mode);
                CurrentMode = mode;
            }
            catch
            {
                // Service may not be reachable
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
            // TODO: Toggle AllowLocalSubnet on the server config
            // This requires GetServerConfig + modify + SetServerConfig round-trip.
            // For now this is a placeholder.
        }

        [RelayCommand]
        private void ToggleHostsBlocklist()
        {
            // TODO: Toggle EnableHostsBlocklist on the server config
            // This requires GetServerConfig + modify + SetServerConfig round-trip.
            // For now this is a placeholder.
        }

        [RelayCommand]
        private void ToggleLock()
        {
            try
            {
                if (IsLocked)
                {
                    // TODO: Show password dialog, then call _controller.TryUnlockServer(pwd)
                    // For now, just attempt with empty password (will fail if password is set)
                }
                else
                {
                    _controller.LockServer();
                    IsLocked = true;
                }
            }
            catch
            {
                // Service may not be reachable
            }
        }

        [RelayCommand]
        private void Quit()
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
