using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace pylorak.TinyWall.Views
{
    /// <summary>
    /// A modern Win11-style popup menu window shown from the tray icon.
    /// Replaces the old NativeMenu with a styled borderless window.
    /// </summary>
    public partial class TrayMenuWindow : Window
    {
        // Current state passed in from App
        private FirewallMode _currentMode;
        private bool _isLocked;
        private bool _isLocalSubnetAllowed;
        private bool _isHostsBlocklistEnabled;
        private ThemeVariant _currentTheme = ThemeVariant.Default;

        // Events raised to App for action handling
        public event Action<FirewallMode>? ModeChangeRequested;
        public event Action? ManageRequested;
        public event Action? ConnectionsRequested;
        public event Action? WhitelistExeRequested;
        public event Action? WhitelistProcessRequested;
        public event Action? WhitelistWindowRequested;
        public event Action? ToggleLocalSubnetRequested;
        public event Action? ToggleHostsBlocklistRequested;
        public event Action? ToggleLockRequested;
        public event Action<ThemeVariant>? ThemeChangeRequested;
        public event Action? QuitRequested;

        public TrayMenuWindow()
        {
            InitializeComponent();
            Deactivated += OnWindowDeactivated;
        }

        /// <summary>
        /// Sets the current state and updates all visual indicators.
        /// Call before showing the window.
        /// </summary>
        public void SetState(
            FirewallMode mode,
            bool isLocked,
            bool localSubnetAllowed,
            bool hostsBlocklistEnabled,
            ThemeVariant currentTheme,
            string trafficRateText)
        {
            _currentMode = mode;
            _isLocked = isLocked;
            _isLocalSubnetAllowed = localSubnetAllowed;
            _isHostsBlocklistEnabled = hostsBlocklistEnabled;
            _currentTheme = currentTheme;

            // Traffic rate
            TxtTrafficRate.Text = trafficRateText;

            // Mode radio indicators
            IndModeNormal.Text = mode == FirewallMode.Normal ? "\u25CF" : "\u25CB";
            IndModeBlockAll.Text = mode == FirewallMode.BlockAll ? "\u25CF" : "\u25CB";
            IndModeAllowOutgoing.Text = mode == FirewallMode.AllowOutgoing ? "\u25CF" : "\u25CB";
            IndModeDisabled.Text = mode == FirewallMode.Disabled ? "\u25CF" : "\u25CB";
            IndModeAutolearn.Text = mode == FirewallMode.Learning ? "\u25CF" : "\u25CB";

            // Mode labels from resources
            TxtModeNormal.Text = pylorak.TinyWall.Resources.Messages.FirewallModeNormal;
            TxtModeBlockAll.Text = pylorak.TinyWall.Resources.Messages.FirewallModeBlockAll;
            TxtModeAllowOutgoing.Text = pylorak.TinyWall.Resources.Messages.FirewallModeAllowOut;
            TxtModeDisabled.Text = pylorak.TinyWall.Resources.Messages.FirewallModeDisabled;
            TxtModeAutolearn.Text = pylorak.TinyWall.Resources.Messages.FirewallModeLearn;

            // Checkbox indicators
            IndLocalSubnet.Text = localSubnetAllowed ? "\u2611" : "\u2610";
            IndHostsBlocklist.Text = hostsBlocklistEnabled ? "\u2611" : "\u2610";

            // Lock text and icon
            TxtLock.Text = isLocked ? pylorak.TinyWall.Resources.Messages.Unlock : pylorak.TinyWall.Resources.Messages.Lock;
            TxtLockIcon.Text = isLocked ? "\U0001F512" : "\U0001F513";

            // Theme radio indicators
            IndThemeSystem.Text = currentTheme == ThemeVariant.Default ? "\u25CF" : "\u25CB";
            IndThemeLight.Text = currentTheme == ThemeVariant.Light ? "\u25CF" : "\u25CB";
            IndThemeDark.Text = currentTheme == ThemeVariant.Dark ? "\u25CF" : "\u25CB";
        }

        /// <summary>
        /// Shows the tray menu at the specified screen position, adjusted to stay on-screen.
        /// </summary>
        public void ShowAt(PixelPoint cursorPosition)
        {
            // Collapse all submenus initially
            PnlModeItems.IsVisible = false;
            TxtModeArrow.Text = "\u25B8";
            PnlWhitelistItems.IsVisible = false;
            TxtWhitelistArrow.Text = "\u25B8";
            PnlThemeItems.IsVisible = false;
            TxtThemeArrow.Text = "\u25B8";

            // Position will be adjusted after the window is measured
            Position = cursorPosition;
            Show();
            Activate();

            // Adjust position after layout so the window stays on screen
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AdjustPosition(cursorPosition);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private void AdjustPosition(PixelPoint cursor)
        {
            var screen = Screens.ScreenFromPoint(cursor);
            if (screen == null) return;

            var workArea = screen.WorkingArea;
            var scaling = screen.Scaling;

            int winW = (int)(Bounds.Width * scaling);
            int winH = (int)(Bounds.Height * scaling);

            int x = cursor.X;
            int y = cursor.Y;

            // If the menu would go off the right edge, flip left
            if (x + winW > workArea.Right)
                x = workArea.Right - winW;

            // If the menu would go off the bottom, show above cursor
            if (y + winH > workArea.Bottom)
                y = cursor.Y - winH;

            // Clamp to work area
            if (x < workArea.X) x = workArea.X;
            if (y < workArea.Y) y = workArea.Y;

            Position = new PixelPoint(x, y);
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            Close();
        }

        // ---------- Submenu expand/collapse ----------

        private void OnModeExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlModeItems.IsVisible = !PnlModeItems.IsVisible;
            TxtModeArrow.Text = PnlModeItems.IsVisible ? "\u25BE" : "\u25B8";
        }

        private void OnWhitelistExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlWhitelistItems.IsVisible = !PnlWhitelistItems.IsVisible;
            TxtWhitelistArrow.Text = PnlWhitelistItems.IsVisible ? "\u25BE" : "\u25B8";
        }

        private void OnThemeExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlThemeItems.IsVisible = !PnlThemeItems.IsVisible;
            TxtThemeArrow.Text = PnlThemeItems.IsVisible ? "\u25BE" : "\u25B8";
        }

        // ---------- Mode clicks ----------

        private void OnModeNormalClick(object? sender, RoutedEventArgs e)
        {
            ModeChangeRequested?.Invoke(FirewallMode.Normal);
            Close();
        }

        private void OnModeBlockAllClick(object? sender, RoutedEventArgs e)
        {
            ModeChangeRequested?.Invoke(FirewallMode.BlockAll);
            Close();
        }

        private void OnModeAllowOutgoingClick(object? sender, RoutedEventArgs e)
        {
            ModeChangeRequested?.Invoke(FirewallMode.AllowOutgoing);
            Close();
        }

        private void OnModeDisabledClick(object? sender, RoutedEventArgs e)
        {
            ModeChangeRequested?.Invoke(FirewallMode.Disabled);
            Close();
        }

        private void OnModeAutolearnClick(object? sender, RoutedEventArgs e)
        {
            ModeChangeRequested?.Invoke(FirewallMode.Learning);
            Close();
        }

        // ---------- Action clicks ----------

        private void OnManageClick(object? sender, RoutedEventArgs e)
        {
            ManageRequested?.Invoke();
            Close();
        }

        private void OnConnectionsClick(object? sender, RoutedEventArgs e)
        {
            ConnectionsRequested?.Invoke();
            Close();
        }

        // ---------- Whitelist clicks ----------

        private void OnWhitelistExeClick(object? sender, RoutedEventArgs e)
        {
            WhitelistExeRequested?.Invoke();
            Close();
        }

        private void OnWhitelistProcessClick(object? sender, RoutedEventArgs e)
        {
            WhitelistProcessRequested?.Invoke();
            Close();
        }

        private void OnWhitelistWindowClick(object? sender, RoutedEventArgs e)
        {
            WhitelistWindowRequested?.Invoke();
            Close();
        }

        // ---------- Toggle clicks ----------

        private void OnAllowLocalSubnetClick(object? sender, RoutedEventArgs e)
        {
            ToggleLocalSubnetRequested?.Invoke();
            Close();
        }

        private void OnEnableHostsBlocklistClick(object? sender, RoutedEventArgs e)
        {
            ToggleHostsBlocklistRequested?.Invoke();
            Close();
        }

        // ---------- Lock click ----------

        private void OnLockClick(object? sender, RoutedEventArgs e)
        {
            ToggleLockRequested?.Invoke();
            Close();
        }

        // ---------- Theme clicks ----------

        private void OnThemeSystemClick(object? sender, RoutedEventArgs e)
        {
            ThemeChangeRequested?.Invoke(ThemeVariant.Default);
            Close();
        }

        private void OnThemeLightClick(object? sender, RoutedEventArgs e)
        {
            ThemeChangeRequested?.Invoke(ThemeVariant.Light);
            Close();
        }

        private void OnThemeDarkClick(object? sender, RoutedEventArgs e)
        {
            ThemeChangeRequested?.Invoke(ThemeVariant.Dark);
            Close();
        }

        // ---------- Quit click ----------

        private void OnQuitClick(object? sender, RoutedEventArgs e)
        {
            QuitRequested?.Invoke();
            Close();
        }
    }
}
