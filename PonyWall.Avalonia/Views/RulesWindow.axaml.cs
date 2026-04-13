using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using pylorak.TinyWall.DatabaseClasses;
using pylorak.TinyWall.Filtering;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class RulesWindow : Window
    {
        private readonly Controller? _controller;
        private ServerConfiguration _config;
        private readonly ObservableCollection<RuleRowViewModel> _allRows = new();
        private readonly ObservableCollection<RuleRowViewModel> _filteredRows = new();
        private string _filterText = string.Empty;

        public RulesWindow() : this(null, new ServerConfiguration()) { }

        internal RulesWindow(Controller? controller, ServerConfiguration config)
        {
            InitializeComponent();
            _controller = controller;
            _config = config;
            dataGrid.ItemsSource = _filteredRows;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Rebuild();
        }

        // ================================================================
        // UI event handlers
        // ================================================================

        private void TxtFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _filterText = (txtFilter.Text ?? string.Empty).Trim();
            ApplyFilter();
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            RefreshConfig();
            Rebuild();
        }

        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var selected = dataGrid.SelectedItem as RuleRowViewModel;
            mnuModify.IsEnabled = selected?.IsEditable == true;
            mnuDelete.IsEnabled = selected?.IsEditable == true;
        }

        // ================================================================
        // CRUD handlers
        // ================================================================

        private async void MnuCreate_Click(object? sender, RoutedEventArgs e)
        {
            var result = await RuleEditorWindow.ShowCreateDialog(this);
            if (result == null) return;

            ApplyNewException(result);
        }

        private async void MnuModify_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as RuleRowViewModel;
            if (selected?.IsEditable != true || selected.ParentException == null)
                return;

            var result = await RuleEditorWindow.ShowModifyDialog(this, selected);
            if (result == null) return;

            // Remove the old exception, add the new one
            _config.ActiveProfile.AppExceptions.RemoveAll(
                ex => ex.Id == selected.ParentException.Id);
            ApplyNewException(result);
        }

        private void MnuDelete_Click(object? sender, RoutedEventArgs e)
        {
            var selected = dataGrid.SelectedItem as RuleRowViewModel;
            if (selected?.IsEditable != true || selected.ParentException == null)
                return;

            _config.ActiveProfile.AppExceptions.RemoveAll(
                ex => ex.Id == selected.ParentException.Id);
            PushConfigAndRefresh();
        }

        private void ApplyNewException(FirewallExceptionV3 exception)
        {
            _config.ActiveProfile.AddExceptions(
                new List<FirewallExceptionV3> { exception });
            PushConfigAndRefresh();
        }

        private void PushConfigAndRefresh()
        {
            if (_controller == null) return;

            var prevCursor = Cursor;
            Cursor = new Cursor(StandardCursorType.Wait);
            try
            {
                Guid changeset = Guid.Empty;
                // Re-read the changeset so we don't conflict
                _controller.GetServerConfig(out _, out _, ref changeset);
                var resp = _controller.SetServerConfig(_config, changeset);
                if (resp.Type == MessageType.PUT_SETTINGS)
                {
                    var putResp = (TwMessagePutSettings)resp;
                    // Re-read the live config after the service applies it
                    // so we see exactly what the service has (it may normalize).
                    Guid cs = Guid.Empty;
                    _controller.GetServerConfig(out var freshConfig, out _, ref cs);
                    if (freshConfig != null)
                        _config = freshConfig;
                    Rebuild();
                }
                else
                {
                    NotificationService.Notify(
                        pylorak.TinyWall.Resources.Messages.OperationFailed,
                        NotificationLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
                NotificationService.Notify(
                    "Failed to apply rule change: " + ex.Message,
                    NotificationLevel.Error);
            }
            finally
            {
                Cursor = prevCursor;
            }
        }

        private void RefreshConfig()
        {
            if (_controller == null) return;
            Guid cs = Guid.Empty;
            _controller.GetServerConfig(out var fresh, out _, ref cs);
            if (fresh != null) _config = fresh;
        }

        // ================================================================
        // Rule flattening
        // ================================================================

        private void Rebuild()
        {
            _allRows.Clear();
            _filteredRows.Clear();

            var db = ServiceGlobals.AppDatabase;
            var profile = _config.ActiveProfile;

            if (_config.Blocklists.EnableBlocklists && _config.Blocklists.EnablePortBlocklist)
            {
                var app = db?.GetApplicationByName("Malware Ports");
                if (app != null)
                    FlattenAppComponents(app, "Blocklist", editable: false);
            }

            foreach (string name in profile.SpecialExceptions)
            {
                var app = db?.GetApplicationByName(name);
                if (app != null)
                    FlattenAppComponents(app, $"Special: {name.Replace('_', ' ')}", editable: false);
            }

            foreach (var ex in profile.AppExceptions)
            {
                FlattenException(ex, "User", editable: true);
            }

            ApplyFilter();
        }

        private void FlattenAppComponents(Application app, string source, bool editable)
        {
            foreach (var id in app.Components)
            {
                List<ExceptionSubject> foundSubjects;
                try { foundSubjects = id.SearchForFile(); }
                catch { foundSubjects = new List<ExceptionSubject>(); }

                if (foundSubjects.Count == 0 && id.Subject is ExecutableSubject)
                    foundSubjects.Add(id.Subject);

                foreach (var subject in foundSubjects)
                {
                    var ex = id.InstantiateException(subject);
                    FlattenException(ex, source, editable);
                }
            }
        }

        private void FlattenException(FirewallExceptionV3 ex, string source, bool editable)
        {
            string appDisplay = string.Empty;
            string appFullPath = string.Empty;
            string serviceName = string.Empty;

            switch (ex.Subject)
            {
                case ServiceSubject srv:
                    appDisplay = Path.GetFileName(srv.ExecutablePath);
                    appFullPath = srv.ExecutablePath;
                    serviceName = srv.ServiceName;
                    break;
                case ExecutableSubject exe:
                    appDisplay = Path.GetFileName(exe.ExecutablePath);
                    appFullPath = exe.ExecutablePath;
                    break;
                case AppContainerSubject uwp:
                    appDisplay = !string.IsNullOrEmpty(uwp.DisplayName) ? uwp.DisplayName : uwp.Sid;
                    appFullPath = uwp.Sid;
                    break;
                case GlobalSubject:
                    appDisplay = "(all applications)";
                    break;
            }

            switch (ex.Policy)
            {
                case HardBlockPolicy:
                    _allRows.Add(new RuleRowViewModel
                    {
                        App = appDisplay, AppFullPath = appFullPath, Service = serviceName,
                        Action = "Block", Direction = "Any", Protocol = "Any",
                        RemoteAddresses = "*", RemotePorts = "*", LocalPorts = "*",
                        Source = source, RuleName = "Hard block",
                        ParentException = editable ? ex : null, IsEditable = editable,
                    });
                    break;

                case UnrestrictedPolicy unrestricted:
                    _allRows.Add(new RuleRowViewModel
                    {
                        App = appDisplay, AppFullPath = appFullPath, Service = serviceName,
                        Action = "Allow", Direction = "Any", Protocol = "Any",
                        RemoteAddresses = unrestricted.LocalNetworkOnly ? "LocalSubnet" : "*",
                        RemotePorts = "*", LocalPorts = "*",
                        Source = source, RuleName = "Unrestricted",
                        ParentException = editable ? ex : null, IsEditable = editable,
                    });
                    break;

                case TcpUdpPolicy tcp:
                    AddTcpUdpRow(appDisplay, appFullPath, serviceName, source, "TCP Out",
                        tcp.AllowedRemoteTcpConnectPorts, ex, editable);
                    AddTcpUdpRow(appDisplay, appFullPath, serviceName, source, "UDP Out",
                        tcp.AllowedRemoteUdpConnectPorts, ex, editable);
                    AddTcpUdpRow(appDisplay, appFullPath, serviceName, source, "TCP Listen",
                        tcp.AllowedLocalTcpListenerPorts, ex, editable);
                    AddTcpUdpRow(appDisplay, appFullPath, serviceName, source, "UDP Listen",
                        tcp.AllowedLocalUdpListenerPorts, ex, editable);
                    break;

                case RuleListPolicy ruleList:
                    foreach (var r in ruleList.Rules)
                    {
                        _allRows.Add(new RuleRowViewModel
                        {
                            App = appDisplay, AppFullPath = appFullPath, Service = serviceName,
                            Action = r.Action == RuleAction.Allow ? "Allow" : "Block",
                            Direction = r.Direction switch
                            {
                                RuleDirection.In => "In",
                                RuleDirection.Out => "Out",
                                RuleDirection.InOut => "Both",
                                _ => "?"
                            },
                            Protocol = r.Protocol switch
                            {
                                Protocol.TCP => "TCP",
                                Protocol.UDP => "UDP",
                                Protocol.TcpUdp => "TCP+UDP",
                                Protocol.ICMPv4 => "ICMPv4",
                                Protocol.ICMPv6 => "ICMPv6",
                                Protocol.Any => "Any",
                                _ => r.Protocol.ToString()
                            },
                            RemoteAddresses = r.RemoteAddresses ?? "*",
                            RemotePorts = r.RemotePorts ?? "*",
                            LocalPorts = r.LocalPorts ?? "*",
                            Source = source, RuleName = r.Name ?? string.Empty,
                            ParentException = editable ? ex : null,
                            ParentRuleDef = editable ? r : null,
                            IsEditable = editable,
                        });
                    }
                    break;
            }
        }

        private void AddTcpUdpRow(string app, string appFullPath, string service,
            string source, string label, string? ports,
            FirewallExceptionV3 ex, bool editable)
        {
            if (string.IsNullOrEmpty(ports)) return;

            bool isListen = label.Contains("Listen");
            _allRows.Add(new RuleRowViewModel
            {
                App = app, AppFullPath = appFullPath, Service = service,
                Action = "Allow",
                Direction = isListen ? "In" : "Out",
                Protocol = label.StartsWith("TCP") ? "TCP" : "UDP",
                RemoteAddresses = "*",
                RemotePorts = isListen ? "*" : ports,
                LocalPorts = isListen ? ports : "*",
                Source = source, RuleName = label,
                ParentException = editable ? ex : null, IsEditable = editable,
            });
        }

        // ================================================================
        // Filter
        // ================================================================

        private void ApplyFilter()
        {
            var filter = QueryFilter.Parse(_filterText);
            _filteredRows.Clear();
            foreach (var row in _allRows)
            {
                if (filter.Matches(
                    row.App, row.Service, row.Action, row.Direction,
                    row.Protocol, row.RemoteAddresses, row.RemotePorts,
                    row.LocalPorts, row.Source, row.RuleName))
                {
                    _filteredRows.Add(row);
                }
            }
            txtStatus.Text = $"Showing {_filteredRows.Count} of {_allRows.Count} rules.";
        }

        // ================================================================
        // Public entry point
        // ================================================================

        internal static void ShowRules(Controller? controller, ServerConfiguration config)
        {
            var window = new RulesWindow(controller, config);
            window.Show();
        }
    }
}
