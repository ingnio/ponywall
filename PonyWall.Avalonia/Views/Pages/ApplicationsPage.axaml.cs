using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using pylorak.TinyWall.Filtering;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class ApplicationsPage : UserControl, INavigablePage
    {
        private Controller? _controller;
        private ServerConfiguration? _config;

        private readonly ObservableCollection<ExceptionListItemViewModel> _allExceptions = new();
        private readonly ObservableCollection<ExceptionListItemViewModel> _filteredExceptions = new();

        public ApplicationsPage()
        {
            InitializeComponent();
        }

        public void Initialize(Controller? controller, ServerConfiguration? config)
        {
            _controller = controller;
            _config = config;

            dgExceptions.ItemsSource = _filteredExceptions;
            dgExceptions.SelectionChanged += DgExceptions_SelectionChanged;
        }

        public void OnNavigatedTo()
        {
            RefreshConfigFromService();
            RebuildExceptionsList();
        }

        public void OnNavigatedFrom()
        {
            // Push any pending config changes when leaving the page
            PushConfig();
        }

        // ========== Config helpers ==========

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
                    RefreshConfigFromService();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        // ========== Exception list helpers ==========

        private void RebuildExceptionsList()
        {
            if (_config == null) return;

            _allExceptions.Clear();
            var packageList = new UwpPackageList();

            foreach (FirewallExceptionV3 ex in _config.ActiveProfile.AppExceptions)
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
                if (filter.Matches(item.Name, item.Type, item.Path))
                {
                    _filteredExceptions.Add(item);
                }
            }

            UpdateExceptionButtonStates();
            txtStatus.Text = $"Showing {_filteredExceptions.Count} of {_allExceptions.Count} applications.";
        }

        private void UpdateExceptionButtonStates()
        {
            bool anySelected = dgExceptions.SelectedItems.Count > 0;
            bool singleSelected = dgExceptions.SelectedItems.Count == 1;
            btnAppModify.IsEnabled = singleSelected;
            btnAppRemove.IsEnabled = anySelected;
        }

        // ========== Event Handlers ==========

        private void TxtExceptionFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyExceptionFilter();
        }

        private void DgExceptions_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateExceptionButtonStates();
        }

        private async void BtnAppAutoDetect_Click(object? sender, RoutedEventArgs e)
        {
            if (_config == null) return;
            try
            {
                var detected = await AppFinderWindow.DetectApps();
                if (detected.Count > 0)
                {
                    _config.ActiveProfile.AddExceptions(detected);
                    RebuildExceptionsList();
                    PushConfig();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private async void BtnAppAdd_Click(object? sender, RoutedEventArgs e)
        {
            if (_config == null) return;
            try
            {
                var result = await ApplicationExceptionWindow.EditException(FirewallExceptionV3.Default);
                if (result != null && result.Count > 0)
                {
                    _config.ActiveProfile.AddExceptions(result);
                    RebuildExceptionsList();
                    PushConfig();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private async void BtnAppModify_Click(object? sender, RoutedEventArgs e)
        {
            if (_config == null) return;
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
                    _config.ActiveProfile.AppExceptions.Remove(oldEx);
                    _config.ActiveProfile.AddExceptions(result);
                    RebuildExceptionsList();
                    PushConfig();
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnAppRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (_config == null) return;
            var selectedItems = dgExceptions.SelectedItems
                .OfType<ExceptionListItemViewModel>()
                .ToList();

            foreach (var item in selectedItems)
            {
                _config.ActiveProfile.AppExceptions.Remove(item.Exception);
            }

            RebuildExceptionsList();
            PushConfig();
        }

        private void BtnAppRemoveAll_Click(object? sender, RoutedEventArgs e)
        {
            if (_config == null) return;

            var parentWindow = TopLevel.GetTopLevel(this) as Window;

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
                _config!.ActiveProfile.AppExceptions.Clear();
                RebuildExceptionsList();
                PushConfig();
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
            if (_config == null) return;
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
                        var imported = SerializationHelper.DeserializeFromFile(filePath, new ConfigContainer(), true);
                        _config = imported.Service;
                        RebuildExceptionsList();
                        PushConfig();
                    }
                    catch (Exception ex)
                    {
                        Utils.LogException(ex, Utils.LOG_ID_GUI);
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
            if (_config == null) return;
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var ctrl = WindowStatePersistence.GetOrLoadController();
                var exportContainer = new ConfigContainer(_config, ctrl);

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
                    SerializationHelper.SerializeToFile(exportContainer, filePath);
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }
    }
}
