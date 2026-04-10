using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Win32;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class ServicesWindow : Window
    {
        private ServiceSubject? _selectedService;

        public ServiceSubject? SelectedService => _selectedService;

        public ServicesWindow()
        {
            InitializeComponent();

            dataGrid.SelectionChanged += DataGrid_SelectionChanged;
            dataGrid.DoubleTapped += DataGrid_DoubleTapped;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadServices();
        }

        private void LoadServices()
        {
            var items = new List<ServiceItemViewModel>();
            ServiceController[] services = ServiceController.GetServices();
            try
            {
                for (int i = 0; i < services.Length; ++i)
                {
                    try
                    {
                        var srv = services[i];
                        items.Add(new ServiceItemViewModel(
                            srv.DisplayName,
                            srv.ServiceName,
                            GetServiceExecutable(srv.ServiceName)));
                    }
                    catch
                    {
                        // Ignore services we cannot inspect
                    }
                }
            }
            finally
            {
                foreach (var srv in services)
                    srv.Dispose();
            }

            dataGrid.ItemsSource = new ObservableCollection<ServiceItemViewModel>(
                items.OrderBy(vm => vm.DisplayName));
        }

        internal static string GetServiceExecutable(string serviceName)
        {
            string imagePath = string.Empty;
            using (RegistryKey keyHKLM = Registry.LocalMachine)
            {
                using var key = keyHKLM.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName);
                if (key == null)
                    return string.Empty;

                var value = key.GetValue("ImagePath");
                if (value is not string strValue)
                    return string.Empty;

                imagePath = strValue;
            }

            // Remove quotes
            imagePath = imagePath.Replace("\"", string.Empty);

            // ImagePath often contains command line arguments.
            // Try to get only the executable path.
            // We use a heuristic approach where we strip off
            // parts of the string (each delimited by spaces)
            // one-by-one, each time checking if we have a valid file path.
            while (true)
            {
                if (System.IO.File.Exists(imagePath))
                    return imagePath;

                int idx = imagePath.LastIndexOf(' ');
                if (idx == -1)
                    break;

                imagePath = imagePath.Substring(0, idx);
            }

            // Could not find executable path
            return string.Empty;
        }

        private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            btnOK.IsEnabled = dataGrid.SelectedItems.Count > 0;
        }

        private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (btnOK.IsEnabled)
            {
                PopulateSelection();
                Close(true);
            }
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            PopulateSelection();
            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void PopulateSelection()
        {
            if (dataGrid.SelectedItem is ServiceItemViewModel vm
                && !string.IsNullOrEmpty(vm.ServiceName)
                && !string.IsNullOrEmpty(vm.ExecutablePath))
            {
                _selectedService = new ServiceSubject(vm.ExecutablePath, vm.ServiceName);
            }
        }

        /// <summary>
        /// Shows the services window and returns the selected service.
        /// Returns null if the user cancels.
        /// </summary>
        internal static async Task<ServiceSubject?> ChooseService()
        {
            var tcs = new TaskCompletionSource<ServiceSubject?>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new ServicesWindow();
                window.Closed += (_, _) =>
                {
                    tcs.TrySetResult(window.SelectedService);
                };
                window.Show();
            });

            return await tcs.Task;
        }
    }
}
