using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using pylorak.TinyWall.History;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class HistoryWindow : Window
    {
        private const int PageSize = 500;
        private static readonly TimeSpan LiveTailInterval = TimeSpan.FromSeconds(3);

        private readonly ObservableCollection<HistoryRowViewModel> _rows = new();
        private readonly Dictionary<long, FirewallEventRecord> _recordsById = new();
        private readonly Controller? _controller;
        private readonly string? _initialFilter;
        private HistoryReader? _reader;
        private DispatcherTimer? _liveTailTimer;
        private string _searchText = string.Empty;
        private bool _suppressSelectionChanged;

        // Parameterless ctor required by Avalonia XAML designer; not used at runtime.
        public HistoryWindow() : this(null, null) { }

        internal HistoryWindow(Controller? controller, string? initialFilter = null)
        {
            InitializeComponent();
            _controller = controller;
            _initialFilter = initialFilter;
            dataGrid.ItemsSource = _rows;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            try
            {
                _reader = new HistoryReader();
                if (!_reader.IsAvailable)
                {
                    txtStatus.Text = "History database not available. Is the service running?";
                    return;
                }

                if (!string.IsNullOrEmpty(_initialFilter))
                {
                    // Setting txtFilter.Text fires TextChanged → updates
                    // _searchText. We then trigger reload explicitly so
                    // there's no race with the dispatcher.
                    txtFilter.Text = _initialFilter;
                    _searchText = _initialFilter;
                }

                ReloadAsync();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Failed to open history.db: " + ex.Message;
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopLiveTail();
            _reader?.Dispose();
            _reader = null;
            base.OnClosed(e);
        }

        private void ChkLiveTail_Changed(object? sender, RoutedEventArgs e)
        {
            if (chkLiveTail.IsChecked == true)
                StartLiveTail();
            else
                StopLiveTail();
        }

        private void StartLiveTail()
        {
            if (_liveTailTimer is not null) return;
            _liveTailTimer = new DispatcherTimer { Interval = LiveTailInterval };
            _liveTailTimer.Tick += LiveTailTimer_Tick;
            _liveTailTimer.Start();
        }

        private void StopLiveTail()
        {
            if (_liveTailTimer is null) return;
            _liveTailTimer.Stop();
            _liveTailTimer.Tick -= LiveTailTimer_Tick;
            _liveTailTimer = null;
        }

        private void LiveTailTimer_Tick(object? sender, EventArgs e)
        {
            if (chkLiveTail.IsChecked == true)
                ReloadAsync();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.F5)
                ReloadAsync();
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => ReloadAsync();

        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (_reader is null || _recordsById.Count == 0)
            {
                txtStatus.Text = "Nothing to export.";
                return;
            }

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is null) return;

                var defaultName = "tinywall-history-" +
                    DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss",
                        System.Globalization.CultureInfo.InvariantCulture) + ".json";

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = "Export firewall history",
                        SuggestedFileName = defaultName,
                        DefaultExtension = "json",
                        FileTypeChoices = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("JSON bundle")
                            {
                                Patterns = new[] { "*.json" }
                            }
                        }
                    });

                if (file is null) return;

                // Snapshot the records in the order they currently appear
                // (newest first) so the export matches what the user sees.
                var records = new List<FirewallEventRecord>(_rows.Count);
                foreach (var row in _rows)
                {
                    if (_recordsById.TryGetValue(row.Id, out var rec))
                        records.Add(rec);
                }

                int written = 0;
                await Task.Run(() =>
                {
                    using var stream = File.Create(file.Path.LocalPath);
                    written = HistoryExporter.Export(
                        records,
                        _reader,
                        stream,
                        toolName: "PonyWall",
                        toolVersion: typeof(HistoryWindow).Assembly.GetName().Version?.ToString());
                }).ConfigureAwait(true);

                txtStatus.Text = $"Exported {written} events to {file.Name}.";
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                txtStatus.Text = "Export failed: " + ex.Message;
            }
        }

        private void Filter_Changed(object? sender, SelectionChangedEventArgs e) => ReloadAsync();

        private void TxtFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchText = (txtFilter.Text ?? string.Empty).Trim();
            ReloadAsync();
        }

        private async void ReloadAsync()
        {
            if (_reader is null || !_reader.IsAvailable)
                return;

            var filter = BuildFilter();

            // Remember the row the user had selected so we can restore the
            // drill-down pane after the refresh — important for live tail,
            // where rebuilding the grid wholesale would otherwise close the
            // selection on every tick.
            long? previouslySelectedId = (dataGrid.SelectedItem as HistoryRowViewModel)?.Id;

            txtStatus.Text = "Loading…";

            try
            {
                var (rows, total) = await Task.Run(() =>
                {
                    var r = _reader.GetRecentEvents(PageSize, 0, filter);
                    var c = _reader.CountEvents(filter);
                    return (r, c);
                }).ConfigureAwait(true);

                // Suppress SelectionChanged while we tear down + repopulate
                // the rows collection — Avalonia raises it with stale items
                // mid-rebuild and we don't want to thrash the drill-down
                // pane. We re-set the selection AFTER releasing suppression
                // so the handler runs once with the new record.
                HistoryRowViewModel? rowToReselect = null;
                _suppressSelectionChanged = true;
                try
                {
                    _rows.Clear();
                    _recordsById.Clear();
                    foreach (var rec in rows)
                    {
                        var vm = HistoryRowViewModel.FromRecord(rec);
                        _rows.Add(vm);
                        _recordsById[rec.Id] = rec;
                        if (previouslySelectedId.HasValue && rec.Id == previouslySelectedId.Value)
                            rowToReselect = vm;
                    }
                }
                finally
                {
                    _suppressSelectionChanged = false;
                }

                if (rowToReselect is not null)
                {
                    // Triggers DataGrid_SelectionChanged → drill-down rebind.
                    dataGrid.SelectedItem = rowToReselect;
                }
                else
                {
                    ClearDetailsPane();
                }

                txtStatus.Text = $"Showing {_rows.Count} of {total} events (newest first, capped at {PageSize}).";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Reload failed: " + ex.Message;
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private HistoryFilter BuildFilter()
        {
            var f = new HistoryFilter();

            if (!string.IsNullOrWhiteSpace(_searchText))
                f.SearchText = _searchText;

            switch (cbAction.SelectedIndex)
            {
                case 1: f.Action = EventAction.Block; break;
                case 2: f.Action = EventAction.Allow; break;
            }

            if (cbReason.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out var reasonInt))
                f.Reason = (ReasonId)reasonInt;

            if (cbTimeRange.SelectedItem is ComboBoxItem timeItem
                && timeItem.Tag is string timeTagStr
                && long.TryParse(timeTagStr, out var secondsBack)
                && secondsBack > 0)
            {
                f.SinceUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - secondsBack * 1000L;
            }

            return f;
        }

        private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // The reload path clears + repopulates the rows collection,
            // which raises SelectionChanged with stale rows mid-rebuild.
            // Suppress recomputation during that window.
            if (_suppressSelectionChanged)
                return;

            if (dataGrid.SelectedItem is not HistoryRowViewModel row)
            {
                ClearDetailsPane();
                return;
            }

            if (!_recordsById.TryGetValue(row.Id, out var record) || _reader is null)
            {
                ClearDetailsPane();
                return;
            }

            try
            {
                var config = _reader.GetRulesetConfig(record.RulesetId);
                Explanation? explanation = null;
                string? hint = null;

                if (config is null)
                {
                    hint = $"Ruleset snapshot #{record.RulesetId} could not be loaded — evidence and near-miss details are unavailable for this row.";
                }
                else
                {
                    explanation = ExplanationService.ExplainAgainst(record, config, record.ModeAtEvent);
                }

                var vm = HistoryDetailsViewModel.FromRecord(record, explanation, hint);
                pnlDetails.DataContext = vm;
                pnlDetails.IsVisible = true;
                txtEmptyState.IsVisible = false;
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                var vm = HistoryDetailsViewModel.FromRecord(record, null,
                    "Failed to build explanation: " + ex.Message);
                pnlDetails.DataContext = vm;
                pnlDetails.IsVisible = true;
                txtEmptyState.IsVisible = false;
            }
        }

        private void ClearDetailsPane()
        {
            pnlDetails.DataContext = null;
            pnlDetails.IsVisible = false;
            txtEmptyState.IsVisible = true;
        }

        // ---------- Context menu ----------

        private FirewallEventRecord? GetSelectedRecord()
        {
            if (dataGrid.SelectedItem is not HistoryRowViewModel row)
                return null;
            return _recordsById.TryGetValue(row.Id, out var rec) ? rec : null;
        }

        private void RowContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec is null)
            {
                e.Cancel = true;
                return;
            }

            bool hasPath = !string.IsNullOrEmpty(rec.AppPath) && File.Exists(rec.AppPath);
            bool hasRemote = !string.IsNullOrEmpty(rec.RemoteIp);

            mnuCreateException.IsEnabled = hasPath && _controller != null;
            mnuCopyAppPath.IsEnabled = !string.IsNullOrEmpty(rec.AppPath);
            mnuCopyRemote.IsEnabled = hasRemote;
            mnuOpenFolder.IsEnabled = hasPath;
            mnuVirusTotal.IsEnabled = hasPath;
            mnuGoogleFilename.IsEnabled = !string.IsNullOrEmpty(rec.AppName);
            mnuGoogleRemote.IsEnabled = hasRemote;
        }

        private void MnuCreateException_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec is null || _controller is null || string.IsNullOrEmpty(rec.AppPath))
                return;

            try
            {
                var subject = new ExecutableSubject(rec.AppPath);
                var exceptions = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subject, false, out _);
                if (exceptions == null || exceptions.Count == 0)
                    exceptions = new List<FirewallExceptionV3> { new FirewallExceptionV3(subject, new TcpUdpPolicy(true)) };

                Guid changeset = Guid.Empty;
                _controller.GetServerConfig(out var config, out _, ref changeset);
                if (config == null)
                {
                    NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
                    return;
                }

                config.ActiveProfile.AddExceptions(exceptions);
                var resp = _controller.SetServerConfig(config, changeset);
                if (resp.Type == MessageType.PUT_SETTINGS)
                {
                    NotificationService.Notify(
                        string.Format(CultureInfo.CurrentCulture,
                            pylorak.TinyWall.Resources.Messages.FirewallRulesForUnrecognizedChanged,
                            exceptions[0].Subject.ToString()));
                }
                else
                {
                    NotificationService.Notify(pylorak.TinyWall.Resources.Messages.OperationFailed, NotificationLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify("Failed to create exception: " + ex.Message, NotificationLevel.Error);
            }
        }

        private async void MnuCopyAppPath_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec?.AppPath is null) return;
            await CopyToClipboardAsync(rec.AppPath);
        }

        private async void MnuCopyRemote_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec?.RemoteIp is null) return;
            string text = rec.RemotePort > 0 ? $"{rec.RemoteIp}:{rec.RemotePort}" : rec.RemoteIp;
            await CopyToClipboardAsync(text);
        }

        private async Task CopyToClipboardAsync(string text)
        {
            try
            {
                var clip = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clip != null)
                    await clip.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify("Clipboard error: " + ex.Message, NotificationLevel.Error);
            }
        }

        private void MnuOpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec?.AppPath is null || !File.Exists(rec.AppPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{rec.AppPath}\"") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify("Could not open folder: " + ex.Message, NotificationLevel.Error);
            }
        }

        private void MnuVirusTotal_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (rec?.AppPath is null || !File.Exists(rec.AppPath)) return;
            try
            {
                string hash = Hasher.HashFile(rec.AppPath);
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.virustotal.com/latest-scan/{0}", hash);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify("VirusTotal lookup failed: " + ex.Message, NotificationLevel.Error);
            }
        }

        private void MnuGoogleFilename_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (string.IsNullOrEmpty(rec?.AppName)) return;
            try
            {
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.google.com/search?q={0}", Uri.EscapeDataString(rec.AppName!));
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void MnuGoogleRemote_Click(object? sender, RoutedEventArgs e)
        {
            var rec = GetSelectedRecord();
            if (string.IsNullOrEmpty(rec?.RemoteIp)) return;
            try
            {
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.google.com/search?q={0}", Uri.EscapeDataString(rec.RemoteIp!));
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        /// <summary>Shows the history window non-modally.</summary>
        internal static void ShowHistory(Controller controller)
        {
            var window = new HistoryWindow(controller);
            window.Show();
        }

        /// <summary>
        /// Shows the history window with a pre-populated text filter.
        /// Used by the toast body-click handler to drop the user into
        /// the same set of rows that triggered the notification.
        /// </summary>
        internal static void ShowHistoryFiltered(Controller controller, string initialFilter)
        {
            var window = new HistoryWindow(controller, initialFilter);
            window.Show();
            window.Activate();
        }
    }
}
