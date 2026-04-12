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
using pylorak.TinyWall.Filtering;
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

            // Restore persisted window geometry + column widths.
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Restore(
                    this,
                    ctrl.ConnFormWindowLocX,
                    ctrl.ConnFormWindowLocY,
                    ctrl.ConnFormWindowWidth,
                    ctrl.ConnFormWindowHeight,
                    ctrl.ConnFormWindowState);
                WindowStatePersistence.RestoreColumnWidths(dataGrid, ctrl.ConnFormColumnWidths);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            _enableListUpdate = true;
            UpdateList();

            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                var ctrl = WindowStatePersistence.GetOrLoadController();
                WindowStatePersistence.Capture(
                    this,
                    ref ctrl.ConnFormWindowLocX,
                    ref ctrl.ConnFormWindowLocY,
                    ref ctrl.ConnFormWindowWidth,
                    ref ctrl.ConnFormWindowHeight,
                    ref ctrl.ConnFormWindowState);
                WindowStatePersistence.CaptureColumnWidths(dataGrid, ctrl.ConnFormColumnWidths);
                ctrl.Save();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
                _autoRefreshTimer = null;
            }
            base.OnClosed(e);
        }

        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (chkAutoRefresh.IsChecked == true)
                    UpdateList();
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
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
            var filter = QueryFilter.Parse(_filterText);
            foreach (var item in _allConnections)
            {
                // Same nine fields the old substring-match used. QueryFilter
                // handles empty/whitespace/null input as match-everything, so
                // no special case for the unfiltered path here.
                if (filter.Matches(
                        item.ProcessName,
                        item.LocalAddress,
                        item.RemoteAddress,
                        item.LocalPort,
                        item.RemotePort,
                        item.Protocol,
                        item.State,
                        item.Services,
                        item.Path))
                {
                    _connections.Add(item);
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

            // Query all four tables up front. The Active / Listen views use
            // them conditionally below, but we ALSO use them as a fallback to
            // recover PIDs for blocked firewall-log entries — see the big
            // comment on _flowPidLookup below. Without this, the Services
            // column is empty for every svchost row in the Blocked view
            // because the FWPM net-event callback doesn't expose process IDs.
            TcpTable tcp4Table = NetStat.GetExtendedTcp4Table(false);
            TcpTable tcp6Table = NetStat.GetExtendedTcp6Table(false);
            UdpTable udp4Table = NetStat.GetExtendedUdp4Table(false);
            UdpTable udp6Table = NetStat.GetExtendedUdp6Table(false);

            // TCP4 table
            foreach (TcpRow tcpRow in tcp4Table)
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
            foreach (TcpRow tcpRow in tcp6Table)
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

                foreach (UdpRow udpRow in udp4Table)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }

                foreach (UdpRow udpRow in udp6Table)
                {
                    var path = GetPathFromPidCached(procCache, udpRow.ProcessId, _controller);
                    var pi = ProcessInfo.Create(udpRow.ProcessId, path, packageList, servicePids);
                    AddRow(rows, pi, "UDP", udpRow.LocalEndPoint, dummyEP, "Listen", now, RuleDirection.Invalid);
                }
            }

            // Build a (Protocol, localIp, localPort) → PID lookup used to
            // recover owning PIDs for blocked firewall-log entries. The WFP
            // FWPM_NET_EVENT_HEADER doesn't carry a process id at all — the
            // service-side WfpNetEventCallback fills FirewallLogEntry.ProcessId
            // with 0 for every blocked row. That cascades through
            // ServicePidMap.GetServicesInPid(0) returning an empty set, and
            // AddRow writing an empty Services column. Long-lived UDP bindings
            // (Dnscache, mDNS, LLMNR, NetBIOS, SSDP — i.e. the svchost rows
            // the user actually sees in a fresh PonyWall install) still have
            // their socket in the UDP table with a correct PID, so we can
            // recover it by 5-tuple intersection.
            //
            // Key by the stringified local address so we don't have to care
            // about IPv4-mapped-v6 quirks or scope-id differences between
            // the two tables.
            var flowPidLookup = new Dictionary<(Protocol proto, string ip, int port), uint>();
            void AddPidToLookup(Protocol proto, IPEndPoint ep, uint pid)
            {
                if (pid == 0) return;
                var key = (proto, ep.Address.ToString(), ep.Port);
                // First-writer-wins: the Windows TCP/UDP tables can legitimately
                // have duplicate local endpoints for different sockets (e.g.
                // TCP TIME_WAIT lingering next to a fresh LISTEN on the same
                // port). We'd rather return the wrong PID than no PID at all,
                // and the first-writer almost always matches.
                if (!flowPidLookup.ContainsKey(key))
                    flowPidLookup[key] = pid;
            }
            foreach (TcpRow r in tcp4Table) AddPidToLookup(Protocol.TCP, r.LocalEndPoint, r.ProcessId);
            foreach (TcpRow r in tcp6Table) AddPidToLookup(Protocol.TCP, r.LocalEndPoint, r.ProcessId);
            foreach (UdpRow r in udp4Table) AddPidToLookup(Protocol.UDP, r.LocalEndPoint, r.ProcessId);
            foreach (UdpRow r in udp6Table) AddPidToLookup(Protocol.UDP, r.LocalEndPoint, r.ProcessId);

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

                // Local helper: resolve a blocked entry's PID from
                // flowPidLookup, trying the exact (proto, ip, port) key
                // first, then the wildcard-bound variants (0.0.0.0 / :: for
                // UDP sockets bound to all interfaces). Returns 0 if no
                // match, leaving entry.ProcessId untouched.
                uint RecoverPid(Protocol proto, string? localIp, int localPort)
                {
                    if (string.IsNullOrEmpty(localIp) || localPort == 0)
                        return 0;
                    if (flowPidLookup.TryGetValue((proto, localIp, localPort), out uint pid))
                        return pid;
                    // UDP bindings on 0.0.0.0 / :: receive on every interface.
                    // A multicast/broadcast datagram sent from such a socket
                    // will have a concrete local IP in the WFP event but the
                    // table entry shows the wildcard.
                    if (flowPidLookup.TryGetValue((proto, "0.0.0.0", localPort), out pid))
                        return pid;
                    if (flowPidLookup.TryGetValue((proto, "::", localPort), out pid))
                        return pid;
                    return 0;
                }

                for (int i = 0; i < filteredLog.Count; ++i)
                {
                    FirewallLogEntry entry = filteredLog[i];

                    // Correct path capitalization
                    entry.AppPath = Utils.GetExactPath(entry.AppPath);

                    // Recover the owning PID from the live TCP/UDP tables
                    // if the WFP callback didn't give us one (which is
                    // always the case on the blocked path — see the
                    // flowPidLookup comment above). Only fills in ProcessId
                    // when it's currently 0; we trust non-zero values that
                    // came from the Security audit log in Learning mode.
                    if (entry.ProcessId == 0)
                    {
                        uint recovered = RecoverPid(entry.Protocol, entry.LocalIp, entry.LocalPort);
                        if (recovered != 0)
                            entry.ProcessId = recovered;
                    }

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
                    ProcessInfo = pi,
                    RawDirection = dir
                });
            }
            catch (Exception ex)
            {
                // Most probably process ID has become invalid.
                // Simply do not add item to the list.
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            UpdateList();
        }

        private void BtnClear_Click(object? sender, RoutedEventArgs e)
        {
            _allConnections.Clear();
            _connections.Clear();
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

            mnuCloseProcess.IsEnabled = selected.Pid != 0;

            // "This destination only" needs a non-empty remote address to
            // build a scoped RuleDef. For listening sockets and flows with
            // no remote endpoint the option is greyed out.
            bool hasRemote = !string.IsNullOrEmpty(selected.RemoteAddress)
                && selected.RemoteAddress != "::"
                && selected.RemoteAddress != "0.0.0.0";
            bool hasPath = !string.IsNullOrEmpty(selected.Path) && selected.Path != "System";
            mnuAllowThisDest.IsEnabled = hasPath && hasRemote;
            mnuAllowAnywhere.IsEnabled = hasPath;
            mnuBlockThisDest.IsEnabled = hasPath && hasRemote;
            mnuBlockAnywhere.IsEnabled = hasPath;
        }

        // ===== Allow / Block context menu handlers =====

        private void MnuAllowThisDest_Click(object? sender, RoutedEventArgs e)
            => ApplyRuleFromContextMenu(ScopeKind.ThisDest, RuleAction.Allow);

        private void MnuAllowAnywhere_Click(object? sender, RoutedEventArgs e)
            => ApplyRuleFromContextMenu(ScopeKind.Anywhere, RuleAction.Allow);

        private void MnuBlockThisDest_Click(object? sender, RoutedEventArgs e)
            => ApplyRuleFromContextMenu(ScopeKind.ThisDest, RuleAction.Block);

        private void MnuBlockAnywhere_Click(object? sender, RoutedEventArgs e)
            => ApplyRuleFromContextMenu(ScopeKind.Anywhere, RuleAction.Block);

        private enum ScopeKind { ThisDest, Anywhere }

        private void ApplyRuleFromContextMenu(ScopeKind scope, RuleAction action)
        {
            var selected = dataGrid.SelectedItems.Cast<ConnectionRowViewModel>().ToList();
            if (selected.Count == 0) return;

            var prevCursor = Cursor;
            Cursor = new Cursor(StandardCursorType.Wait);
            try
            {
                var exceptions = new List<FirewallExceptionV3>();
                foreach (var row in selected)
                {
                    var pi = row.ProcessInfo;
                    if (pi == null) continue;

                    ExceptionSubject subject;
                    if (pi.Package.HasValue)
                        subject = new AppContainerSubject(pi.Package.Value.Sid, pi.Package.Value.Name, pi.Package.Value.Publisher, pi.Package.Value.PublisherId);
                    else if (!string.IsNullOrEmpty(pi.Path) && pi.Path != "System")
                        subject = new ExecutableSubject(pi.Path);
                    else
                        continue;

                    if (scope == ScopeKind.Anywhere && action == RuleAction.Allow)
                    {
                        // Broad allow — check if the app database has a known
                        // template for this app (e.g., Chrome's predefined
                        // rules), which is narrower and more appropriate than
                        // a raw unrestricted policy. Fall back to unrestricted
                        // only if no template is found.
                        var appExceptions = ServiceGlobals.AppDatabase?.GetExceptionsForApp(subject, false, out _);
                        if (appExceptions != null && appExceptions.Count > 0)
                            exceptions.AddRange(appExceptions);
                        else
                            exceptions.Add(new FirewallExceptionV3(subject, new UnrestrictedPolicy { LocalNetworkOnly = false }));
                    }
                    else if (scope == ScopeKind.Anywhere && action == RuleAction.Block)
                    {
                        exceptions.Add(new FirewallExceptionV3(subject, HardBlockPolicy.Instance));
                    }
                    else if (scope == ScopeKind.ThisDest)
                    {
                        // Scoped rule narrowed by the row's flow tuple.
                        // Same pattern as the toast's BuildScopedRuleListPolicy.
                        Protocol proto = Enum.TryParse<Protocol>(row.Protocol, true, out var p) ? p : Protocol.Any;
                        int remotePort = int.TryParse(row.RemotePort, out var rp) ? rp : 0;
                        RuleDirection dir = row.RawDirection;
                        if (dir == RuleDirection.Invalid) dir = RuleDirection.InOut;

                        string ruleName = (action == RuleAction.Allow ? "Allow " : "Block ")
                            + System.IO.Path.GetFileName(pi.Path) + " to "
                            + (string.IsNullOrEmpty(row.RemoteAddress) ? "unknown" : row.RemoteAddress);

                        var rule = new RuleDef
                        {
                            Name = ruleName,
                            Action = action,
                            Application = pi.Path,
                            RemoteAddresses = string.IsNullOrEmpty(row.RemoteAddress) ? null : row.RemoteAddress,
                            RemotePorts = remotePort > 0
                                ? remotePort.ToString(CultureInfo.InvariantCulture)
                                : null,
                            Protocol = proto,
                            Direction = dir,
                        };

                        var policy = new RuleListPolicy
                        {
                            Rules = new List<RuleDef> { rule }
                        };
                        exceptions.Add(new FirewallExceptionV3(subject, policy));
                    }
                }

                if (exceptions.Count == 0) return;

                Guid changeset = Guid.Empty;
                _controller.GetServerConfig(out var config, out ServerState? _dummyState, ref changeset);
                if (config != null)
                {
                    // For "Anywhere" scope, replace any existing exceptions for
                    // the same subject so broad rules don't stack on top of
                    // prior scoped ones (same convention as the toast's
                    // replaceExisting flag). Scoped rules accumulate.
                    if (scope == ScopeKind.Anywhere)
                    {
                        foreach (var ex in exceptions)
                            config.ActiveProfile.AppExceptions.RemoveAll(e => e.Subject.Equals(ex.Subject));
                    }

                    config.ActiveProfile.AddExceptions(exceptions);
                    var resp = _controller.SetServerConfig(config, changeset);
                    if (resp.Type == MessageType.PUT_SETTINGS)
                    {
                        UpdateList();
                    }
                    else
                    {
                        NotificationService.Notify(pylorak.TinyWall.Resources.Messages.OperationFailed, NotificationLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CommunicationWithTheServiceError, NotificationLevel.Error);
            }
            finally
            {
                Cursor = prevCursor;
            }
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
                    catch (Exception ex)
                    {
                        // Could not close process
                        Utils.LogException(ex, Utils.LOG_ID_GUI);
                    }
                }
                catch (Exception ex)
                {
                    // The app has probably already quit.
                    Utils.LogException(ex, Utils.LOG_ID_GUI);
                }
            }

            UpdateList();
        }

        private async void MnuCopyRemoteAddress_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var selected = dataGrid.SelectedItem as ConnectionRowViewModel;
                if (selected is null)
                    return;

                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(selected.RemoteAddress);
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify($"Error: {ex.Message}", NotificationLevel.Error);
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
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(
                    string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.CannotGetPathOfProcess),
                    NotificationLevel.Error);
                Utils.LogException(ex, Utils.LOG_ID_GUI);
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
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CannotGetPathOfProcess, NotificationLevel.Error);
                Utils.LogException(ex, Utils.LOG_ID_GUI);
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
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CannotGetPathOfProcess, NotificationLevel.Error);
                Utils.LogException(ex, Utils.LOG_ID_GUI);
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
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                NotificationService.Notify(pylorak.TinyWall.Resources.Messages.CannotGetPathOfProcess, NotificationLevel.Error);
                Utils.LogException(ex, Utils.LOG_ID_GUI);
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
