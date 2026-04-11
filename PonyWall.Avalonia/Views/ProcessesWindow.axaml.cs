using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using pylorak.TinyWall.ViewModels;
using pylorak.Windows;

namespace pylorak.TinyWall.Views
{
    public partial class ProcessesWindow : Window
    {
        private readonly bool _multiSelect;

        internal List<ProcessInfo> Selection { get; } = new();

        public ProcessesWindow(bool multiSelect)
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
        public ProcessesWindow() : this(false) { }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Restore(
                    this,
                    ctrl.ProcessesFormWindowLocX,
                    ctrl.ProcessesFormWindowLocY,
                    ctrl.ProcessesFormWindowWidth,
                    ctrl.ProcessesFormWindowHeight,
                    ctrl.ProcessesFormWindowState);
                WindowStatePersistence.RestoreColumnWidths(dataGrid, ctrl.ProcessesFormColumnWidths);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            LoadProcesses();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Capture(
                    this,
                    ref ctrl.ProcessesFormWindowLocX,
                    ref ctrl.ProcessesFormWindowLocY,
                    ref ctrl.ProcessesFormWindowWidth,
                    ref ctrl.ProcessesFormWindowHeight,
                    ref ctrl.ProcessesFormWindowState);
                WindowStatePersistence.CaptureColumnWidths(dataGrid, ctrl.ProcessesFormColumnWidths);
                ctrl.Save();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            base.OnClosing(e);
        }

        private void LoadProcesses()
        {
            var packageList = new UwpPackageList();
            var servicePids = new ServicePidMap();
            var procs = Process.GetProcesses();
            var items = new List<ProcessItemViewModel>();

            for (int i = 0; i < procs.Length; ++i)
            {
                try
                {
                    using var p = procs[i];
                    var pid = unchecked((uint)p.Id);
                    var info = ProcessInfo.Create(pid, packageList, servicePids);

                    if (string.IsNullOrEmpty(info.Path))
                        continue;

                    // Deduplicate by (Package, Path, Services)
                    bool skip = false;
                    for (int j = 0; j < items.Count; ++j)
                    {
                        var existing = items[j].ProcessInfo;
                        if ((info.Package == existing.Package)
                            && (info.Path == existing.Path)
                            && (info.Services.SetEquals(existing.Services)))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                        continue;

                    items.Add(new ProcessItemViewModel(info));
                }
                catch (Exception ex)
                {
                    // Ignore processes we cannot inspect
                    Utils.LogException(ex, Utils.LOG_ID_GUI);
                }
            }

            dataGrid.ItemsSource = new ObservableCollection<ProcessItemViewModel>(
                items.OrderBy(vm => vm.Name));
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
            Selection.Clear();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item is ProcessItemViewModel vm)
                    Selection.Add(vm.ProcessInfo);
            }
        }

        /// <summary>
        /// Shows the processes window and returns the selected processes.
        /// Returns an empty list if the user cancels.
        /// </summary>
        internal static async Task<List<ProcessInfo>> ChooseProcess(bool multiSelect)
        {
            var tcs = new TaskCompletionSource<List<ProcessInfo>>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new ProcessesWindow(multiSelect);
                window.Closed += (_, _) =>
                {
                    tcs.TrySetResult(window.Selection.Count > 0
                        ? window.Selection
                        : new List<ProcessInfo>());
                };
                window.Show();
            });

            return await tcs.Task;
        }
    }
}
