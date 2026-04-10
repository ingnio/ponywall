using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using pylorak.TinyWall.ViewModels;
using pylorak.Windows;
using pylorak.Windows.NetStat;

namespace pylorak.TinyWall.Views
{
    public partial class ConnectionsWindow : Window
    {
        private readonly Controller _controller;
        private bool _enableListUpdate;
        private readonly ObservableCollection<ConnectionRowViewModel> _allConnections = new();
        private readonly ObservableCollection<ConnectionRowViewModel> _connections = new();
        private DispatcherTimer? _autoRefreshTimer;
        private string _filterText = string.Empty;

        internal ConnectionsWindow(Controller controller)
        {
            InitializeComponent();
            _controller = controller;
            dataGrid.ItemsSource = _connections;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _enableListUpdate = true;
            UpdateList();

            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _autoRefreshTimer.Tick += (_, _) =>
            {
                if (chkAutoRefresh.IsChecked == true)
                    UpdateList();
            };
            _autoRefreshTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoRefreshTimer?.Stop();
            base.OnClosed(e);
        }

        private void ChkAutoRefresh_Changed(object? sender, RoutedEventArgs e) { }

        private void TxtFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _filterText = (txtFilter.Text ?? string.Empty).Trim();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _connections.Clear();
            if (string.IsNullOrEmpty(_filterText))
            {
                foreach (var item in _allConnections)
                    _connections.Add(item);
            }
            else
            {
                var filter = _filterText.ToUpperInvariant();
                foreach (var item in _allConnections)
                {
                    if ((item.ProcessName?.ToUpperInvariant().Contains(filter) == true)
                        || (item.LocalAddress?.ToUpperInvariant().Contains(filter) == true)
                        || (item.RemoteAddress?.ToUpperInvariant().Contains(filter) == true)
                        || (item.LocalPort?.Contains(filter) == true)
                        || (item.RemotePort?.Contains(filter) == true)
                        || (item.Protocol?.ToUpperInvariant().Contains(filter) == true)
                        || (item.State?.ToUpperInvariant().Contains(filter) == true)
                        || (item.Services?.ToUpperInvariant().Contains(filter) == true)
                        || (item.Path?.ToUpperInvariant().Contains(filter) == true))
                    {
                        _connections.Add(item);
                    }
                }
            }
        }

        private static string GetPathFromPidCached(Dictionary<uint, string> cache, uint pid, Controller controller)
        {
            if (cache.TryGetValue(pid, out string? path))
                return path;

            string ret = Utils.GetPathOfProcessUseTwService(pid, controller);
            cache.Add(pid, ret);
            return ret;
        }

        private void UpdateList()
        {
            if (!_enableListUpdate)
                return;

            var fwLogRequest = _controller.BeginReadFwLog();

            var packageList = new UwpPackageList();
            var rows = new List<ConnectionRowViewModel>();
            var procCache = new Dictionary<uint, string>();
            var servicePids = new ServicePidMap();

            DateTime now = DateTime.Now;

            // TCP4 table
            TcpTable tcpTable = NetStat.GetExtendedTcp4Table(false);
            foreach (TcpRow tcpRow in tcpTable)
            {
                if ((chkShowListen.IsChecked == true && tcpRow.State == TcpState.Listen)
                    || (chkShowActive.IsChecked == true && tcpRow.State != TcpState.Listen))
                {
                    var path = GetPathFromPidCached(procCache, tcpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(tcpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "TCP", tcpRow.LocalEndPoint, tcpRow.RemoteEndPoint, tcpRow.State.ToString(), now, RuleDirection.Invalid);
                }
            }

            // TCP6 table
            tcpTable = NetStat.GetExtendedTcp6Table(false);
            foreach (TcpRow tcpRow in tcpTable)
            {
                if ((chkShowListen.IsChecked == true && tcpRow.State == TcpState.Listen)
                    || (chkShowActive.IsChecked == true && tcpRow.State != TcpState.Listen))
                {
                    var path = GetPathFromPidCached(procCache, tcpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(tcpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "TCP", tcpRow.LocalEndPoint, tcpRow.RemoteEndPoint, tcpRow.State.ToString(), now, RuleDirection.Invalid);
                }
            }

            // UDP tables (only when showing listening)
            if (chkShowListen.IsChecked == true)
            {
                var dummyEP = new IPEndPoint(0, 0);

                var udpTable = NetStat.GetExtendedUdp4Table(false);
                foreach (UdpRow udpRow in udpTable)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }

                udpTable = NetStat.GetExtendedUdp6Table(false);
                foreach (UdpRow udpRow in udpTable)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }
            }

            // Firewall log (blocked entries)
            var fwLog = Controller.EndReadFwLog(fwLogRequest.Response);

            if (chkShowBlocked.IsChecked == true)
            {
                // Try to resolve PIDs heuristically
                var processPathInfoMap = new Dictionary<string, List<ProcessSnapshotEntry>>();
                foreach (var p in ProcessManager.CreateToolhelp32SnapshotExtended())
                {
                    if (string.IsNullOrEmpty(p.ImagePath))
                        continue;

                    var key = p.ImagePath.ToLowerInvariant();
                    if (!processPathInfoMap.ContainsKey(key))
                        processPathInfoMap.Add(key, new List<ProcessSnapshotEntry>());
                    processPathInfoMap[key].Add(p);
                }

                foreach (var entry in fwLog)
                {
                    if (entry.AppPath is null) continue;

                    var key = entry.AppPath.ToLowerInvariant();
                    if (!processPathInfoMap.ContainsKey(key))
                        continue;

                    var p = processPathInfoMap[key];
                    if (p.Count == 1 && p[0].CreationTime < entry.Timestamp.ToFileTime())
                        entry.ProcessId = p[0].ProcessId;
                }

                var filteredLog = new List<FirewallLogEntry>();
                TimeSpan refSpan = TimeSpan.FromMinutes(5);
                for (int i = 0; i < fwLog.Length; ++i)
                {
                    FirewallLogEntry newEntry = fwLog[i];

                    // Ignore log entries older than refSpan
                    TimeSpan span = now - newEntry.Timestamp;
                    if (span > refSpan)
                        continue;

                    switch (newEntry.Event)
                    {
                        case EventLogEvent.ALLOWED_LISTEN:
                        case EventLogEvent.ALLOWED_CONNECTION:
                        case EventLogEvent.ALLOWED_LOCAL_BIND:
                        case EventLogEvent.ALLOWED:
                            newEntry.Event = EventLogEvent.ALLOWED;
                            break;

                        case EventLogEvent.BLOCKED_LISTEN:
                        case EventLogEvent.BLOCKED_CONNECTION:
                        case EventLogEvent.BLOCKED_LOCAL_BIND:
                        case EventLogEvent.BLOCKED_PACKET:
                        case EventLogEvent.BLOCKED:
                        {
                            bool matchFound = false;
                            newEntry.Event = EventLogEvent.BLOCKED;

                            for (int j = 0; j < filteredLog.Count; ++j)
                            {
                                FirewallLogEntry oldEntry = filteredLog[j];
                                if (oldEntry.Equals(newEntry, false))
                                {
                                    matchFound = true;
                                    oldEntry.Timestamp = newEntry.Timestamp;
                                    break;
                                }
                            }

                            if (!matchFound)
                                filteredLog.Add(newEntry);
                            break;
                        }
                    }
                }

                for (int i = 0; i < filteredLog.Count; ++i)
                {
                    FirewallLogEntry entry = filteredLog[i];

                    // Correct path capitalization
                    entry.AppPath = Utils.GetExactPath(entry.AppPath);

                    var pi = ProcessInfo.Create(entry.ProcessId, entry.AppPath ?? string.Empty, entry.PackageId, packageList, servicePids);
                    AddRow(rows, pi, entry.Protocol.ToString(), new IPEndPoint(IPAddress.Parse(entry.LocalIp ?? "::"), entry.LocalPort), new IPEndPoint(IPAddress.Parse(entry.RemoteIp ?? "::"), entry.RemotePort), "Blocked", entry.Timestamp, entry.Direction);
                }
            }

            _allConnections.Clear();
            foreach (var row in rows)
                _allConnections.Add(row);

            ApplyFilter();
        }

        private static void AddRow(List<ConnectionRowViewModel> rows, ProcessInfo pi, string protocol, IPEndPoint localEP, IPEndPoint remoteEP, string state, DateTime ts, RuleDirection dir)
        {
            try
            {
                string name = pi.Package.HasValue ? pi.Package.Value.Name : System.IO.Path.GetFileName(pi.Path);
                string title = pi.Pid != 0 ? $"{name} ({pi.Pid})" : $"{name}";

                string direction = dir switch
                {
                    RuleDirection.In => pylorak.TinyWall.Resources.Messages.TrafficIn,
                    RuleDirection.Out => pylorak.TinyWall.Resources.Messages.TrafficOut,
                    _ => string.Empty
                };

                rows.Add(new ConnectionRowViewModel
                {
                    ProcessName = title,
                    Services = pi.Pid != 0 ? string.Join(", ", pi.Services.ToArray()) : string.Empty,
                    Protocol = protocol,
                    LocalPort = localEP.Port.ToString(CultureInfo.InvariantCulture).PadLeft(5),
                    LocalAddress = localEP.Address.ToString(),
                    RemotePort = remoteEP.Port.ToString(CultureInfo.InvariantCulture).PadLeft(5),
                    RemoteAddress = remoteEP.Address.ToString(),
                    State = state,
                    Direction = direction,
                    Timestamp = ts.ToString("yyyy/MM/dd HH:mm:ss"),
                    Pid = pi.Pid,
                    Path = pi.Path,
                    ProcessInfo = pi
                });
            }
            catch
            {
                // Most probably process ID has become invalid.
                // Simply do not add item to the list.
            }
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            UpdateList();
        }

        private void ChkFilter_Changed(object? sender, RoutedEventArgs e)
        {
            UpdateList();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                UpdateList();
                e.Handled = true;
            }
        }

        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null)
            {
                e.Cancel = true;
                return;
            }

            // Disable Close Process if PID is 0
            mnuCloseProcess.IsEnabled = selected.Pid != 0;
        }

        private void MnuUnblock_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder - unblock functionality requires TinyWallController integration
            // which will be wired up in a later phase.
        }

        private void MnuCloseProcess_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItems.Cast<ConnectionRowViewModel>().ToList();
            foreach (var row in selected)
            {
                try
                {
                    using Process proc = Process.GetProcessById(unchecked((int)row.Pid));
                    try
                    {
                        if (!proc.CloseMainWindow())
                            proc.Kill();

                        proc.WaitForExit(5000);
                    }
                    catch (InvalidOperationException)
                    {
                        // The process has already exited.
                    }
                    catch
                    {
                        // Could not close process - ignore in Avalonia port for now
                    }
                }
                catch
                {
                    // The app has probably already quit.
                }
            }

            UpdateList();
        }

        private async void MnuCopyRemoteAddress_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null)
                return;

            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(selected.RemoteAddress);
            }
            catch
            {
                // Fail silently
            }
        }

        private void MnuVirusTotal_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null || string.IsNullOrEmpty(selected.Path))
                return;

            try
            {
                string hash = Hasher.HashFile(selected.Path);
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.virustotal.com/latest-scan/{0}", hash);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Cannot get path of process
            }
        }

        private void MnuProcessLibrary_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null || string.IsNullOrEmpty(selected.Path))
                return;

            try
            {
                string filename = System.IO.Path.GetFileName(selected.Path);
                string url = string.Format(CultureInfo.InvariantCulture, "http://www.processlibrary.com/search/?q={0}", filename);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Cannot get path of process
            }
        }

        private void MnuGoogleFilename_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null || string.IsNullOrEmpty(selected.Path))
                return;

            try
            {
                string filename = System.IO.Path.GetFileName(selected.Path);
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.google.com/search?q={0}", filename);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Cannot get path of process
            }
        }

        private void MnuGoogleRemoteAddress_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
            if (selected is null)
                return;

            try
            {
                string url = string.Format(CultureInfo.InvariantCulture, "https://www.google.com/search?q={0}", selected.RemoteAddress);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Cannot get path of process
            }
        }

        /// <summary>
        /// Shows the connections window non-modally.
        /// </summary>
        internal static void ShowConnections(Controller controller)
        {
            var window = new ConnectionsWindow(controller);
            window.Show();
        }
    }
}
