using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace pylorak.TinyWall.Views
{
    public partial class MainWindow : Window
    {
        private readonly Controller? _controller;
        private ServerConfiguration? _config;
        private readonly Dictionary<string, UserControl> _pages = new();
        private INavigablePage? _activePage;
        private string _activeTag = string.Empty;
        private bool _suppressNavChange;

        public MainWindow() : this(null, null) { }

        internal MainWindow(Controller? controller, ServerConfiguration? config)
        {
            InitializeComponent();
            _controller = controller;
            _config = config;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            string target = ActiveConfig.Controller?.LastNavPage ?? "Dashboard";
            SelectNavByTag(target);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _activePage?.OnNavigatedFrom();

            var ctrl = ActiveConfig.Controller;
            if (ctrl != null)
            {
                ctrl.LastNavPage = _activeTag;
                try { ctrl.Save(); } catch { }
            }

            base.OnClosing(e);
        }

        private void NavList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressNavChange) return;

            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is not ListBoxItem item)
                return;

            var tag = item.Tag as string;
            if (string.IsNullOrEmpty(tag))
                return;

            // Special action items — not pages, just triggers
            if (tag == "Logs")
            {
                OpenLogsFolder();
                RestoreNavSelection(listBox);
                return;
            }
            // Settings is now a real page (no longer a modal)

            // Cross-deselect: if the user clicked in the main list,
            // clear the bottom list's selection and vice versa.
            _suppressNavChange = true;
            if (sender == navList)
                navBottomList.SelectedItem = null;
            else if (sender == navBottomList)
                navList.SelectedItem = null;
            _suppressNavChange = false;

            if (tag != _activeTag)
                NavigateTo(tag);
        }

        private void SelectNavByTag(string tag)
        {
            _suppressNavChange = true;

            // Search both lists
            bool found = false;
            foreach (var item in navList.Items)
            {
                if (item is ListBoxItem lbi && lbi.Tag as string == tag)
                {
                    navList.SelectedItem = lbi;
                    navBottomList.SelectedItem = null;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                foreach (var item in navBottomList.Items)
                {
                    if (item is ListBoxItem lbi && lbi.Tag as string == tag)
                    {
                        navBottomList.SelectedItem = lbi;
                        navList.SelectedItem = null;
                        found = true;
                        break;
                    }
                }
            }

            _suppressNavChange = false;

            if (found && tag != _activeTag)
                NavigateTo(tag);
            else if (!found)
            {
                // Fallback to Dashboard
                navList.SelectedIndex = 0;
                NavigateTo("Dashboard");
            }
        }

        private void NavigateTo(string tag)
        {
            _activePage?.OnNavigatedFrom();

            if (!_pages.TryGetValue(tag, out var page))
            {
                page = CreatePage(tag);
                if (page == null) return;
                _pages[tag] = page;

                if (page is INavigablePage nav)
                    nav.Initialize(_controller, _config);
            }

            pageHost.Content = page;
            _activeTag = tag;
            _activePage = page as INavigablePage;
            _activePage?.OnNavigatedTo();
        }

        private UserControl? CreatePage(string tag)
        {
            return tag switch
            {
                "Dashboard" => new StatsPage(),
                "Connections" => new ConnectionsPage(),
                "History" => new HistoryPage(),
                "Rules" => new RulesPage(),
                "Applications" => new ApplicationsPage(),
                "Settings" => new SettingsPage(),
                "About" => new AboutPage(),
                _ => null,
            };
        }

        private void RestoreNavSelection(ListBox listBox)
        {
            _suppressNavChange = true;
            listBox.SelectedItem = null;
            SelectNavByTag(_activeTag);
            _suppressNavChange = false;
        }

        private static void OpenLogsFolder()
        {
            try
            {
                string logDir = System.IO.Path.Combine(Utils.AppDataPath, "logs");
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        internal void UpdateConfig(ServerConfiguration config)
        {
            _config = config;
        }

        internal static MainWindow ShowMain(Controller? controller, ServerConfiguration? config)
        {
            var window = new MainWindow(controller, config);
            window.Show();
            return window;
        }
    }
}
