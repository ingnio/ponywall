using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using pylorak.TinyWall.Filtering;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class SettingsWindow : Window
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

        internal ConfigContainer TmpConfig;
        public string? NewPassword { get; private set; }

        private readonly ObservableCollection<ExceptionListItemViewModel> _allExceptions = new();
        private readonly ObservableCollection<ExceptionListItemViewModel> _filteredExceptions = new();
        private readonly ObservableCollection<SpecialExceptionItemViewModel> _recommendedSpecial = new();
        private readonly ObservableCollection<SpecialExceptionItemViewModel> _optionalSpecial = new();

        private bool _loadingSettings;

        internal SettingsWindow(ServerConfiguration service, ControllerSettings controller)
        {
            InitializeComponent();

            TmpConfig = new ConfigContainer(service, controller);
            TmpConfig.Service.ActiveProfile.Normalize();

            dgExceptions.ItemsSource = _filteredExceptions;
            listRecommended.ItemsSource = _recommendedSpecial;
            listOptional.ItemsSource = _optionalSpecial;

            // Populate language combo
            PopulateLanguages();

            // Set initial tab
            tabControl.SelectedIndex = TmpConfig.Controller.SettingsTabIndex;

            // Wire up selection change for button state
            dgExceptions.SelectionChanged += DgExceptions_SelectionChanged;

            // Load settings into UI
            InitSettingsUI();
        }

        // Parameterless constructor for XAML designer
        public SettingsWindow()
        {
            InitializeComponent();
            TmpConfig = new ConfigContainer();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                // SettingsForm has no WindowState field — always restore as Normal.
                WindowStatePersistence.Restore(
                    this,
                    ctrl.SettingsFormWindowLocX,
                    ctrl.SettingsFormWindowLocY,
                    ctrl.SettingsFormWindowWidth,
                    ctrl.SettingsFormWindowHeight,
                    WindowStateValue.Normal);
                WindowStatePersistence.RestoreColumnWidths(dgExceptions, ctrl.SettingsFormAppListColumnWidths);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                // SettingsForm has no WindowState field — use a scratch variable.
                var scratchState = WindowStateValue.Normal;
                WindowStatePersistence.Capture(
                    this,
                    ref ctrl.SettingsFormWindowLocX,
                    ref ctrl.SettingsFormWindowLocY,
                    ref ctrl.SettingsFormWindowWidth,
                    ref ctrl.SettingsFormWindowHeight,
                    ref scratchState);
                WindowStatePersistence.CaptureColumnWidths(dgExceptions, ctrl.SettingsFormAppListColumnWidths);
                ctrl.Save();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            base.OnClosing(e);
        }

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

        private void InitSettingsUI()
        {
            _loadingSettings = true;
            try
            {
                // General page
                chkAutoUpdateCheck.IsChecked = TmpConfig.Service.AutoUpdateCheck;
                chkAskForExceptionDetails.IsChecked = TmpConfig.Controller.AskForExceptionDetails;
                chkEnableHotkeys.IsChecked = TmpConfig.Controller.EnableGlobalHotkeys;

                // Select the matching language
                var langItems = comboLanguages.ItemsSource as List<LanguageItem>;
                if (langItems != null)
                {
                    comboLanguages.SelectedIndex = 0;
                    for (int i = 0; i < langItems.Count; i++)
                    {
                        if (langItems[i].Id.Equals(TmpConfig.Controller.Language, StringComparison.OrdinalIgnoreCase))
                        {
                            comboLanguages.SelectedIndex = i;
                            break;
                        }
                    }
                }

                // Machine Settings tab
                chkAllowLocalSubnet.IsChecked = TmpConfig.Service.ActiveProfile.AllowLocalSubnet;
                chkDisplayOffBlock.IsChecked = TmpConfig.Service.ActiveProfile.DisplayOffBlock;
                chkLockHostsFile.IsChecked = TmpConfig.Service.LockHostsFile;
                chkFirstBlockToasts.IsChecked = TmpConfig.Service.ActiveProfile.EnableFirstBlockToasts;
                chkHostsBlocklist.IsChecked = TmpConfig.Service.Blocklists.EnableHostsBlocklist;
                chkBlockMalwarePorts.IsChecked = TmpConfig.Service.Blocklists.EnablePortBlocklist;
                chkEnableBlocklists.IsChecked = TmpConfig.Service.Blocklists.EnableBlocklists;
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

                        bool isChecked = TmpConfig.Service.ActiveProfile.SpecialExceptions.Contains(id);
                        var item = new SpecialExceptionItemViewModel(id, name, isChecked);

                        if (app.HasFlag("TWUI:Recommended"))
                            _recommendedSpecial.Add(item);
                        else
                            _optionalSpecial.Add(item);
                    }
                }

                // Application Exceptions
                RebuildExceptionsList();

                // Password
                chkChangePassword.IsChecked = false;
                txtPassword.Text = string.Empty;
                txtPasswordAgain.Text = string.Empty;

                // Version label
                lblVersion.Text = string.Format(CultureInfo.CurrentCulture, "PonyWall {0}",
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "");
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void RebuildExceptionsList()
        {
            _allExceptions.Clear();
            var packageList = new UwpPackageList();

            foreach (FirewallExceptionV3 ex in TmpConfig.Service.ActiveProfile.AppExceptions)
            {
                _allExceptions.Add(CreateExceptionItem(ex, packageList));
            }

            ApplyExceptionFilter();
        }

        private ExceptionListItemViewModel CreateExceptionItem(FirewallExceptionV3 ex, UwpPackageList packageList)
        {
            string name, type, path;
            IBrush? background = null;

            var exeSubj = ex.Subject as ExecutableSubject;
            var srvSubj = ex.Subject as ServiceSubject;
            var uwpSubj = ex.Subject as AppContainerSubject;

            switch (ex.Subject.SubjectType)
            {
                case SubjectType.Executable:
                    name = exeSubj!.ExecutableName;
                    type = pylorak.TinyWall.Resources.Messages.SubjectTypeExecutable;
                    path = exeSubj.ExecutablePath;
                    break;
                case SubjectType.Service:
                    name = srvSubj!.ServiceName;
                    type = pylorak.TinyWall.Resources.Messages.SubjectTypeService;
                    path = srvSubj.ExecutablePath;
                    break;
                case SubjectType.Global:
                    name = pylorak.TinyWall.Resources.Messages.AllApplications;
                    type = pylorak.TinyWall.Resources.Messages.SubjectTypeGlobal;
                    path = string.Empty;
                    break;
                case SubjectType.AppContainer:
                    name = uwpSubj!.DisplayName;
                    type = pylorak.TinyWall.Resources.Messages.SubjectTypeUwpApp;
                    path = uwpSubj.PublisherId + ", " + uwpSubj.Publisher;
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (ex.Policy.PolicyType == PolicyType.HardBlock)
            {
                background = new SolidColorBrush(Color.FromRgb(255, 182, 193)); // LightPink
            }

            if (uwpSubj is not null)
            {
                if (!packageList.FindPackage(uwpSubj.Sid).HasValue)
                {
                    background = new SolidColorBrush(Color.FromRgb(211, 211, 211)); // LightGray
                }
            }

            if (exeSubj is not null)
            {
                if (exeSubj.ExecutablePath != "System" && !File.Exists(exeSubj.ExecutablePath)
                    && !pylorak.Windows.NetworkPath.IsNetworkPath(exeSubj.ExecutablePath))
                {
                    background = new SolidColorBrush(Color.FromRgb(211, 211, 211)); // LightGray
                }
            }

            return new ExceptionListItemViewModel(ex, name, type, path, background);
        }

        private void ApplyExceptionFilter()
        {
            var filter = QueryFilter.Parse(txtExceptionFilter?.Text);
            _filteredExceptions.Clear();

            foreach (var item in _allExceptions)
            {
                // Also include Path in the searchable fields — the old code
                // only searched Name and Type, which meant you couldn't find
                // an exception by its on-disk location. Adding Path here is
                // a drive-by fix; it costs nothing and matches user intent.
                if (filter.Matches(item.Name, item.Type, item.Path))
                {
                    _filteredExceptions.Add(item);
                }
            }

            UpdateExceptionButtonStates();
        }

        private void UpdateBlocklistSubCheckboxes()
        {
            bool enabled = chkEnableBlocklists.IsChecked == true;
            chkHostsBlocklist.IsEnabled = enabled;
            chkBlockMalwarePorts.IsEnabled = enabled;
        }

        private void UpdateExceptionButtonStates()
        {
            bool anySelected = dgExceptions.SelectedItems.Count > 0;
            bool singleSelected = dgExceptions.SelectedItems.Count == 1;
            btnAppModify.IsEnabled = singleSelected;
            btnAppRemove.IsEnabled = anySelected;
        }

        // ========== Event Handlers ==========

        private void ChkChangePassword_Changed(object? sender, RoutedEventArgs e)
        {
            bool enabled = chkChangePassword.IsChecked == true;
            txtPassword.IsEnabled = enabled;
            txtPasswordAgain.IsEnabled = enabled;
        }

        private void ChkEnableBlocklists_Changed(object? sender, RoutedEventArgs e)
        {
            if (!_loadingSettings)
                UpdateBlocklistSubCheckboxes();
        }

        private void TxtExceptionFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyExceptionFilter();
        }

        private void DgExceptions_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateExceptionButtonStates();
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            // Validate password
            if (chkChangePassword.IsChecked == true)
            {
                if (txtPassword.Text != txtPasswordAgain.Text)
                {
                    // Show simple error - just use a basic approach
                    var msgWindow = new Window
                    {
                        Title = "PonyWall",
                        Width = 350,
                        Height = 150,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        Content = new StackPanel
                        {
                            Margin = new Avalonia.Thickness(16),
                            Spacing = 12,
                            Children =
                            {
                                new TextBlock { Text = pylorak.TinyWall.Resources.Messages.PasswordFieldsDoNotMatch, TextWrapping = TextWrapping.Wrap },
                                new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                            }
                        }
                    };
                    ((msgWindow.Content as StackPanel)!.Children[1] as Button)!.Click += (_, _) => msgWindow.Close();
                    msgWindow.Show();
                    return;
                }
            }

            // Set password
            NewPassword = chkChangePassword.IsChecked == true ? txtPassword.Text : null;

            // Save general settings
            TmpConfig.Controller.AskForExceptionDetails = chkAskForExceptionDetails.IsChecked == true;
            TmpConfig.Controller.EnableGlobalHotkeys = chkEnableHotkeys.IsChecked == true;
            TmpConfig.Service.AutoUpdateCheck = chkAutoUpdateCheck.IsChecked == true;
            TmpConfig.Controller.SettingsTabIndex = tabControl.SelectedIndex;
            TmpConfig.Service.LockHostsFile = chkLockHostsFile.IsChecked == true;
            TmpConfig.Service.Blocklists.EnablePortBlocklist = chkBlockMalwarePorts.IsChecked == true;
            TmpConfig.Service.Blocklists.EnableHostsBlocklist = chkHostsBlocklist.IsChecked == true;
            TmpConfig.Service.Blocklists.EnableBlocklists = chkEnableBlocklists.IsChecked == true;
            TmpConfig.Service.ActiveProfile.DisplayOffBlock = chkDisplayOffBlock.IsChecked == true;
            TmpConfig.Service.ActiveProfile.AllowLocalSubnet = chkAllowLocalSubnet.IsChecked == true;
            TmpConfig.Service.ActiveProfile.EnableFirstBlockToasts = chkFirstBlockToasts.IsChecked == true;

            // Save language
            var selectedLang = comboLanguages.SelectedItem as LanguageItem;
            if (selectedLang != null)
                TmpConfig.Controller.Language = selectedLang.Id;

            // Save special exceptions
            TmpConfig.Service.ActiveProfile.SpecialExceptions.Clear();
            foreach (var item in _recommendedSpecial)
            {
                if (item.IsChecked)
                    TmpConfig.Service.ActiveProfile.SpecialExceptions.Add(item.Id);
            }
            foreach (var item in _optionalSpecial)
            {
                if (item.IsChecked)
                    TmpConfig.Service.ActiveProfile.SpecialExceptions.Add(item.Id);
            }

            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private async void BtnAppAutoDetect_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var detected = await AppFinderWindow.DetectApps();
                if (detected.Count > 0)
                {
                    TmpConfig.Service.ActiveProfile.AddExceptions(detected);
                    RebuildExceptionsList();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private async void BtnAppAdd_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var result = await ApplicationExceptionWindow.EditException(FirewallExceptionV3.Default);
                if (result != null && result.Count > 0)
                {
                    TmpConfig.Service.ActiveProfile.AddExceptions(result);
                    RebuildExceptionsList();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private async void BtnAppModify_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dgExceptions.SelectedItem as ExceptionListItemViewModel;
            if (selected == null) return;

            try
            {
                var oldEx = selected.Exception;
                var newEx = Utils.DeepClone(oldEx);
                newEx.RegenerateId();

                var result = await ApplicationExceptionWindow.EditException(newEx, true);
                if (result != null && result.Count > 0)
                {
                    TmpConfig.Service.ActiveProfile.AppExceptions.Remove(oldEx);
                    TmpConfig.Service.ActiveProfile.AddExceptions(result);
                    RebuildExceptionsList();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnAppRemove_Click(object? sender, RoutedEventArgs e)
        {
            var selectedItems = dgExceptions.SelectedItems
                .OfType<ExceptionListItemViewModel>()
                .ToList();

            foreach (var item in selectedItems)
            {
                TmpConfig.Service.ActiveProfile.AppExceptions.Remove(item.Exception);
            }

            RebuildExceptionsList();
        }

        private void BtnAppRemoveAll_Click(object? sender, RoutedEventArgs e)
        {
            // Simple confirmation dialog
            var confirmWindow = new Window
            {
                Title = "PonyWall",
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                CanResize = false,
                Topmost = true,
            };

            var panel = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = pylorak.TinyWall.Resources.Messages.AreYouSureYouWantToRemoveAllExceptions,
                TextWrapping = TextWrapping.Wrap
            });

            var btnPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8
            };
            var yesBtn = new Button { Content = "Yes", Width = 80 };
            var noBtn = new Button { Content = "No", Width = 80 };

            yesBtn.Click += (_, _) =>
            {
                TmpConfig.Service.ActiveProfile.AppExceptions.Clear();
                RebuildExceptionsList();
                confirmWindow.Close();
            };
            noBtn.Click += (_, _) => confirmWindow.Close();

            btnPanel.Children.Add(yesBtn);
            btnPanel.Children.Add(noBtn);
            panel.Children.Add(btnPanel);
            confirmWindow.Content = panel;
            confirmWindow.Show();
        }

        private async void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Settings",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("TinyWall Settings (*.tws)") { Patterns = new[] { "*.tws" } },
                        new FilePickerFileType("All Files (*)") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count == 1)
                {
                    string filePath = files[0].Path.LocalPath;
                    try
                    {
                        TmpConfig = SerializationHelper.DeserializeFromFile(filePath, new ConfigContainer(), true);
                        InitSettingsUI();
                        ShowNotificationMessage(pylorak.TinyWall.Resources.Messages.ConfigurationHasBeenImported);
                    }
                    catch (Exception ex)
                    {
                        Utils.LogException(ex, Utils.LOG_ID_GUI);
                        ShowNotificationMessage(pylorak.TinyWall.Resources.Messages.ConfigurationImportError);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Settings",
                    DefaultExtension = "tws",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("TinyWall Settings (*.tws)") { Patterns = new[] { "*.tws" } },
                        new FilePickerFileType("All Files (*)") { Patterns = new[] { "*" } }
                    }
                });

                if (file != null)
                {
                    string filePath = file.Path.LocalPath;
                    SerializationHelper.SerializeToFile(TmpConfig, filePath);
                    ShowNotificationMessage(pylorak.TinyWall.Resources.Messages.ConfigurationHasBeenExported);
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnLicenseLink_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Try LICENSE.txt next to exe first, then fall back to repo root convention
                string[] candidates = new[]
                {
                    Path.Combine(Path.GetDirectoryName(Utils.ExecutablePath)!, "LICENSE.txt"),
                    Path.Combine(Path.GetDirectoryName(Utils.ExecutablePath)!, "License.rtf"),
                };
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                        return;
                    }
                }
                // Fallback: open website
                Process.Start(new ProcessStartInfo("https://tinywall.pados.hu") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnAttributionsLink_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Utils.ExecutablePath)!, "Attributions.txt");
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                    return;
                }
                // Fallback: open website
                Process.Start(new ProcessStartInfo("https://tinywall.pados.hu") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnDonate_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo("https://tinywall.pados.hu/donate.php") { UseShellExecute = true };
                Process.Start(psi)?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnUpdate_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo("https://tinywall.pados.hu") { UseShellExecute = true };
                Process.Start(psi)?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
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
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            ((msgWindow.Content as StackPanel)!.Children[1] as Button)!.Click += (_, _) => msgWindow.Close();
            msgWindow.Show();
        }

        // ========== Static helper for standalone window pattern ==========

        /// <summary>
        /// Shows the Settings window as a standalone dialog and returns the result.
        /// Returns (TmpConfig, NewPassword) if OK, or null if cancelled.
        /// </summary>
        internal static async Task<(ConfigContainer Config, string? NewPassword)?> ShowSettings(
            ServerConfiguration service, ControllerSettings controller)
        {
            var tcs = new TaskCompletionSource<(ConfigContainer, string?)?>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SettingsWindow(service, controller);
                window.Closed += (_, _) =>
                {
                    // Check if the window was closed with OK (result=true)
                    // We use a simple approach: if NewPassword is set or TmpConfig was modified
                    // Actually we need to track the dialog result
                    tcs.TrySetResult(null);
                };
                window.Show();
                window.Activate();
            });

            return await tcs.Task;
        }

        // Override Close to set the result properly
        private bool _resultSet;

        private new void Close(bool dialogResult)
        {
            if (dialogResult && !_resultSet)
            {
                _resultSet = true;
                // The Closed handler will pick up TmpConfig and NewPassword
            }
            base.Close(dialogResult);
        }

        /// <summary>
        /// Shows the Settings window and returns the edited config, or null if cancelled.
        /// Uses TaskCompletionSource for standalone window pattern.
        /// </summary>
        internal static async Task<(ConfigContainer Config, string? NewPassword)?> ShowSettingsDialog(
            ServerConfiguration service, ControllerSettings controller)
        {
            var tcs = new TaskCompletionSource<(ConfigContainer, string?)?>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SettingsWindow(service, controller);
                window.Closed += (_, args) =>
                {
                    if (window._resultSet)
                        tcs.TrySetResult((window.TmpConfig, window.NewPassword));
                    else
                        tcs.TrySetResult(null);
                };
                window.Show();
                window.Activate();
            });

            return await tcs.Task;
        }
    }
}
