using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class UwpPackagesWindow : Window
    {
        private readonly bool _multiSelect;

        public List<UwpPackageList.Package> SelectedPackages { get; } = new();

        public UwpPackagesWindow(bool multiSelect)
        {
            _multiSelect = multiSelect;
            InitializeComponent();

            dataGrid.SelectionMode = multiSelect
                ? DataGridSelectionMode.Extended
                : DataGridSelectionMode.Single;

            dataGrid.SelectionChanged += DataGrid_SelectionChanged;
            dataGrid.DoubleTapped += DataGrid_DoubleTapped;
        }

        // Parameterless constructor for Avalonia designer
        public UwpPackagesWindow() : this(false) { }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Restore(
                    this,
                    ctrl.UwpPackagesFormWindowLocX,
                    ctrl.UwpPackagesFormWindowLocY,
                    ctrl.UwpPackagesFormWindowWidth,
                    ctrl.UwpPackagesFormWindowHeight,
                    ctrl.UwpPackagesFormWindowState);
                WindowStatePersistence.RestoreColumnWidths(dataGrid, ctrl.UwpPackagesFormColumnWidths);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            LoadPackages();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Capture(
                    this,
                    ref ctrl.UwpPackagesFormWindowLocX,
                    ref ctrl.UwpPackagesFormWindowLocY,
                    ref ctrl.UwpPackagesFormWindowWidth,
                    ref ctrl.UwpPackagesFormWindowHeight,
                    ref ctrl.UwpPackagesFormWindowState);
                WindowStatePersistence.CaptureColumnWidths(dataGrid, ctrl.UwpPackagesFormColumnWidths);
                ctrl.Save();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            base.OnClosing(e);
        }

        private void LoadPackages()
        {
            var packageList = new UwpPackageList();
            var viewModels = new ObservableCollection<UwpPackageViewModel>(
                packageList
                    .Select(p => new UwpPackageViewModel(p))
                    .OrderBy(vm => vm.Name)
            );
            dataGrid.ItemsSource = viewModels;
        }

        private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            btnOK.IsEnabled = dataGrid.SelectedItems.Count > 0;
        }

        private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (btnOK.IsEnabled)
            {
                PopulateSelectedPackages();
                Close(true);
            }
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            PopulateSelectedPackages();
            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void PopulateSelectedPackages()
        {
            SelectedPackages.Clear();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item is UwpPackageViewModel vm)
                    SelectedPackages.Add(vm.Package);
            }
        }

        /// <summary>
        /// Shows the UWP packages window and returns the selected packages.
        /// Returns an empty list if the user cancels.
        /// </summary>
        internal static async Task<List<UwpPackageList.Package>> ChoosePackage(bool multiSelect)
        {
            var tcs = new TaskCompletionSource<List<UwpPackageList.Package>>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new UwpPackagesWindow(multiSelect);
                window.Closed += (_, _) =>
                {
                    tcs.TrySetResult(window.SelectedPackages.Count > 0
                        ? window.SelectedPackages
                        : new List<UwpPackageList.Package>());
                };
                window.Show();
            });

            return await tcs.Task;
        }
    }
}
