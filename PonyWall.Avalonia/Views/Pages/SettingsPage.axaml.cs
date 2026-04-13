using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class SettingsPage : UserControl, INavigablePage
    {
        // Simple helper class for language combo items
        private sealed class LanguageItem
        {
            public string Id { get; }
            public string DisplayName { get; }

            public LanguageItem(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public override string ToString() => DisplayName;
        }

        private Controller? _controller;
        private ServerConfiguration? _config;

        private readonly ObservableCollection<SpecialExceptionItemViewModel> _recommendedSpecial = new();
        private readonly ObservableCollection<SpecialExceptionItemViewModel> _optionalSpecial = new();

        private bool _loadingSettings;

        public SettingsPage()
        {
            InitializeComponent();
        }

        public void Initialize(Controller? controller, ServerConfiguration? config)
        {
            _controller = controller;
            _config = config;

            listRecommended.ItemsSource = _recommendedSpecial;
            listOptional.ItemsSource = _optionalSpecial;

            PopulateLanguages();

            // Wire up live-save on checkbox/combobox changes
            WireLiveSaveEvents();
        }

        public void OnNavigatedTo()
        {
            // Re-read fresh config from the service each time we navigate in
            RefreshConfigFromService();
            LoadSettingsUI();
        }

        public void OnNavigatedFrom()
        {
            // Save special exceptions + any other pending state on leave
            SaveSpecialExceptions();
            PushConfig();
        }

        // ========== Live-save infrastructure ==========

        /// <summary>
        /// Wire Changed events on controls so that toggling a checkbox or
        /// changing a combobox immediately pushes the config to the service.
        /// </summary>
        private void WireLiveSaveEvents()
        {
            // General
            chkAutoUpdateCheck.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkAskForExceptionDetails.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkEnableHotkeys.IsCheckedChanged += OnLiveSaveCheckChanged;
            comboLanguages.SelectionChanged += OnLiveSaveComboChanged;

            // Security
            chkAllowLocalSubnet.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkDisplayOffBlock.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkLockHostsFile.IsCheckedChanged += OnLiveSaveCheckChanged;

            // Notifications
            chkFirstBlockToasts.IsCheckedChanged += OnLiveSaveCheckChanged;

            // Blocklists
            chkEnableBlocklists.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkHostsBlocklist.IsCheckedChanged += OnLiveSaveCheckChanged;
            chkBlockMalwarePorts.IsCheckedChanged += OnLiveSaveCheckChanged;
        }

        private void OnLiveSaveCheckChanged(object? sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            CollectSettingsToConfig();
            PushConfig();
        }

        private void OnLiveSaveComboChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;
            CollectSettingsToConfig();
            PushConfig();
        }

        /// <summary>
        /// Reads the current UI state into <see cref="_config"/>.
        /// </summary>
        private void CollectSettingsToConfig()
        {
            if (_config == null) return;

            // General
            _config.AutoUpdateCheck = chkAutoUpdateCheck.IsChecked == true;
            _config.LockHostsFile = chkLockHostsFile.IsChecked == true;

            // Security
            _config.ActiveProfile.AllowLocalSubnet = chkAllowLocalSubnet.IsChecked == true;
            _config.ActiveProfile.DisplayOffBlock = chkDisplayOffBlock.IsChecked == true;

            // Notifications
            _config.ActiveProfile.EnableFirstBlockToasts = chkFirstBlockToasts.IsChecked == true;

            // Blocklists
            _config.Blocklists.EnableBlocklists = chkEnableBlocklists.IsChecked == true;
            _config.Blocklists.EnableHostsBlocklist = chkHostsBlocklist.IsChecked == true;
            _config.Blocklists.EnablePortBlocklist = chkBlockMalwarePorts.IsChecked == true;

            // Controller-side settings (saved locally, not pushed to service)
            var ctrl = WindowStatePersistence.GetOrLoadController();
            ctrl.AskForExceptionDetails = chkAskForExceptionDetails.IsChecked == true;
            ctrl.EnableGlobalHotkeys = chkEnableHotkeys.IsChecked == true;
            var selectedLang = comboLanguages.SelectedItem as LanguageItem;
            if (selectedLang != null)
                ctrl.Language = selectedLang.Id;
            ctrl.Save();
        }

        /// <summary>
        /// Pushes the current <see cref="_config"/> to the service via
        /// GetServerConfig (for changeset) + SetServerConfig round-trip.
        /// Runs the pipe I/O on a background thread.
        /// </summary>
        private async void PushConfig()
        {
            if (_controller == null || _config == null) return;

            var controller = _controller;
            var config = _config;

            try
            {
                MessageType respType = MessageType.RESPONSE_ERROR;
                await Task.Run(() =>
                {
                    Guid changeset = Guid.Empty;
                    controller.GetServerConfig(out _, out _, ref changeset);
                    var resp = controller.SetServerConfig(config, changeset);
                    respType = resp.Type;
                }).ConfigureAwait(true);

                if (respType == MessageType.PUT_SETTINGS)
                {
                    // Re-read fresh config so our reference stays in sync
                    RefreshConfigFromService();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        /// <summary>
        /// Re-reads the live server configuration so the local reference
        /// is always up to date.
        /// </summary>
        private void RefreshConfigFromService()
        {
            if (_controller == null) return;
            try
            {
                Guid changeset = Guid.Empty;
                _controller.GetServerConfig(out var fresh, out _, ref changeset);
                if (fresh != null)
                    _config = fresh;
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        // ========== Settings load ==========

        private void PopulateLanguages()
        {
            var languages = new List<LanguageItem>
            {
                new("auto", "Automatic"),
                new("bg", "\u0431\u044a\u043b\u0433\u0430\u0440\u0441\u043a\u0438"),
                new("cs", "\u010ce\u0161tina"),
                new("de", "Deutsch"),
                new("en", "English"),
                new("es", "Espa\u00f1ol"),
                new("fr", "Fran\u00e7ais"),
                new("it", "Italiano"),
                new("he-IL", "\u05e2\u05d1\u05e8\u05d9\u05ea"),
                new("hu", "Magyar"),
                new("nl", "Nederlands"),
                new("pl", "Polski"),
                new("pt-BR", "Portugu\u00eas Brasileiro"),
                new("ru", "\u0420\u0443\u0441\u0441\u043a\u0438\u0439"),
                new("tr", "T\u00fcrk\u00e7e"),
                new("ja", "\u65e5\u672c\u8a9e"),
                new("ko", "\ud55c\uad6d\uc5b4"),
                new("zh", "\u6c49\u8bed"),
            };

            comboLanguages.ItemsSource = languages;
            comboLanguages.SelectedIndex = 0;
        }

        private void LoadSettingsUI()
        {
            if (_config == null) return;

            _loadingSettings = true;
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();

                // General
                chkAutoUpdateCheck.IsChecked = _config.AutoUpdateCheck;
                chkAskForExceptionDetails.IsChecked = ctrl.AskForExceptionDetails;
                chkEnableHotkeys.IsChecked = ctrl.EnableGlobalHotkeys;

                // Select the matching language
                var langItems = comboLanguages.ItemsSource as List<LanguageItem>;
                if (langItems != null)
                {
                    comboLanguages.SelectedIndex = 0;
                    for (int i = 0; i < langItems.Count; i++)
                    {
                        if (langItems[i].Id.Equals(ctrl.Language, StringComparison.OrdinalIgnoreCase))
                        {
                            comboLanguages.SelectedIndex = i;
                            break;
                        }
                    }
                }

                // Security
                chkAllowLocalSubnet.IsChecked = _config.ActiveProfile.AllowLocalSubnet;
                chkDisplayOffBlock.IsChecked = _config.ActiveProfile.DisplayOffBlock;
                chkLockHostsFile.IsChecked = _config.LockHostsFile;

                // Notifications
                chkFirstBlockToasts.IsChecked = _config.ActiveProfile.EnableFirstBlockToasts;

                // Blocklists
                chkHostsBlocklist.IsChecked = _config.Blocklists.EnableHostsBlocklist;
                chkBlockMalwarePorts.IsChecked = _config.Blocklists.EnablePortBlocklist;
                chkEnableBlocklists.IsChecked = _config.Blocklists.EnableBlocklists;
                UpdateBlocklistSubCheckboxes();

                // Special Exceptions
                _recommendedSpecial.Clear();
                _optionalSpecial.Clear();
                foreach (DatabaseClasses.Application app in ServiceGlobals.AppDatabase.KnownApplications)
                {
                    if (app.HasFlag("TWUI:Special") && !app.HasFlag("TWUI:Hidden"))
                    {
                        string id = app.Name;
                        string name = app.LocalizedName;
                        if (string.IsNullOrEmpty(name))
                            name = id.Replace('_', ' ');

                        bool isChecked = _config.ActiveProfile.SpecialExceptions.Contains(id);
                        var item = new SpecialExceptionItemViewModel(id, name, isChecked);

                        if (app.HasFlag("TWUI:Recommended"))
                            _recommendedSpecial.Add(item);
                        else
                            _optionalSpecial.Add(item);
                    }
                }

                // Password
                chkChangePassword.IsChecked = false;
                txtPassword.Text = string.Empty;
                txtPasswordAgain.Text = string.Empty;
                btnApplyPassword.IsEnabled = false;
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        // ========== Special Exceptions helpers ==========

        private void SaveSpecialExceptions()
        {
            if (_config == null) return;

            _config.ActiveProfile.SpecialExceptions.Clear();
            foreach (var item in _recommendedSpecial)
            {
                if (item.IsChecked)
                    _config.ActiveProfile.SpecialExceptions.Add(item.Id);
            }
            foreach (var item in _optionalSpecial)
            {
                if (item.IsChecked)
                    _config.ActiveProfile.SpecialExceptions.Add(item.Id);
            }
        }

        // ========== Blocklist helpers ==========

        private void UpdateBlocklistSubCheckboxes()
        {
            bool enabled = chkEnableBlocklists.IsChecked == true;
            chkHostsBlocklist.IsEnabled = enabled;
            chkBlockMalwarePorts.IsEnabled = enabled;
        }

        // ========== Event Handlers ==========

        private void ChkChangePassword_Changed(object? sender, RoutedEventArgs e)
        {
            bool enabled = chkChangePassword.IsChecked == true;
            txtPassword.IsEnabled = enabled;
            txtPasswordAgain.IsEnabled = enabled;
            btnApplyPassword.IsEnabled = enabled;
        }

        private void BtnApplyPassword_Click(object? sender, RoutedEventArgs e)
        {
            if (_controller == null) return;

            if (txtPassword.Text != txtPasswordAgain.Text)
            {
                ShowNotificationMessage(pylorak.TinyWall.Resources.Messages.PasswordFieldsDoNotMatch);
                return;
            }

            string password = txtPassword.Text ?? string.Empty;
            try
            {
                _controller.SetPassphrase(password);
                ShowNotificationMessage("Password has been updated.");

                // Reset the password fields
                chkChangePassword.IsChecked = false;
                txtPassword.Text = string.Empty;
                txtPasswordAgain.Text = string.Empty;
                btnApplyPassword.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                ShowNotificationMessage("Failed to update password.");
            }
        }

        private void ChkEnableBlocklists_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_loadingSettings)
                UpdateBlocklistSubCheckboxes();
        }

        // ========== Helper methods ==========

        private void ShowNotificationMessage(string message)
        {
            var msgWindow = new Window
            {
                Title = "PonyWall",
                Width = 380,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                CanResize = false,
                Topmost = true,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            ((msgWindow.Content as StackPanel)!.Children[1] as Button)!.Click += (_, _) => msgWindow.Close();
            msgWindow.Show();
        }
    }
}
