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
        private ThemeVariant _currentTheme = ThemeVariant.Default;

        // Events raised to App for action handling
        public event Action<FirewallMode>? ModeChangeRequested;
        public event Action? ManageRequested;
        public event Action? ConnectionsRequested;
        public event Action? HistoryRequested;
        public event Action? StatsRequested;
        public event Action? LogsRequested;
        public event Action? WhitelistExeRequested;
        public event Action? WhitelistProcessRequested;
        public event Action? WhitelistWindowRequested;
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
            ThemeVariant currentTheme,
            string trafficRateText)
        {
            _currentMode = mode;
            _isLocked = isLocked;
            _currentTheme = currentTheme;

            // Traffic rate
            TxtTrafficRate.Text = trafficRateText;

            // Mode radio indicators (Segoe Fluent Icons: E73E = checkmark, empty = unselected)
            IndModeNormal.Text = mode == FirewallMode.Normal ? "\uE73E" : " ";
            IndModeBlockAll.Text = mode == FirewallMode.BlockAll ? "\uE73E" : " ";
            IndModeAllowOutgoing.Text = mode == FirewallMode.AllowOutgoing ? "\uE73E" : " ";
            IndModeDisabled.Text = mode == FirewallMode.Disabled ? "\uE73E" : " ";
            IndModeAutolearn.Text = mode == FirewallMode.Learning ? "\uE73E" : " ";

            // Mode labels from resources
            TxtModeNormal.Text = pylorak.TinyWall.Resources.Messages.FirewallModeNormal;
            TxtModeBlockAll.Text = pylorak.TinyWall.Resources.Messages.FirewallModeBlockAll;
            TxtModeAllowOutgoing.Text = pylorak.TinyWall.Resources.Messages.FirewallModeAllowOut;
            TxtModeDisabled.Text = pylorak.TinyWall.Resources.Messages.FirewallModeDisabled;
            TxtModeAutolearn.Text = pylorak.TinyWall.Resources.Messages.FirewallModeLearn;

            // Lock text and icon (Segoe Fluent Icons: E72E = lock, E785 = unlock)
            TxtLock.Text = isLocked ? pylorak.TinyWall.Resources.Messages.Unlock : pylorak.TinyWall.Resources.Messages.Lock;
            TxtLockIcon.Text = isLocked ? "\uE72E" : "\uE785";

            // Theme radio indicators
            IndThemeSystem.Text = currentTheme == ThemeVariant.Default ? "\uE73E" : " ";
            IndThemeLight.Text = currentTheme == ThemeVariant.Light ? "\uE73E" : " ";
            IndThemeDark.Text = currentTheme == ThemeVariant.Dark ? "\uE73E" : " ";

            // Set background to match Win11 context menu
            UpdateMenuBackground();
        }

        /// <summary>
        /// Shows the tray menu at the specified screen position, adjusted to stay on-screen.
        /// </summary>
        public void ShowAt(PixelPoint cursorPosition)
        {
            // Collapse all submenus initially
            PnlModeItems.IsVisible = false;
            TxtModeArrow.Text = "\uE76C";
            PnlWhitelistItems.IsVisible = false;
            TxtWhitelistArrow.Text = "\uE76C";
            PnlThemeItems.IsVisible = false;
            TxtThemeArrow.Text = "\uE76C";

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

        private void UpdateMenuBackground()
        {
            // Match Win11 Explorer context menu colors
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            MenuBorder.Background = new Avalonia.Media.SolidColorBrush(
                isDark ? Avalonia.Media.Color.Parse("#2D2D2D") : Avalonia.Media.Color.Parse("#F9F9F9"));
            MenuBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(
                isDark ? Avalonia.Media.Color.Parse("#454545") : Avalonia.Media.Color.Parse("#E0E0E0"));
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            Close();
        }

        // ---------- Submenu expand/collapse ----------

        private void OnModeExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlModeItems.IsVisible = !PnlModeItems.IsVisible;
            TxtModeArrow.Text = PnlModeItems.IsVisible ? "\uE70D" : "\uE76C";
        }

        private void OnWhitelistExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlWhitelistItems.IsVisible = !PnlWhitelistItems.IsVisible;
            TxtWhitelistArrow.Text = PnlWhitelistItems.IsVisible ? "\uE70D" : "\uE76C";
        }

        private void OnThemeExpandClick(object? sender, RoutedEventArgs e)
        {
            PnlThemeItems.IsVisible = !PnlThemeItems.IsVisible;
            TxtThemeArrow.Text = PnlThemeItems.IsVisible ? "\uE70D" : "\uE76C";
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

        private void OnHistoryClick(object? sender, RoutedEventArgs e)
        {
            HistoryRequested?.Invoke();
            Close();
        }

        private void OnStatsClick(object? sender, RoutedEventArgs e)
        {
            StatsRequested?.Invoke();
            Close();
        }

        private void OnLogsClick(object? sender, RoutedEventArgs e)
        {
            LogsRequested?.Invoke();
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
