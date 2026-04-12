using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using pylorak.TinyWall.DatabaseClasses;
using pylorak.TinyWall.Filtering;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class RulesWindow : Window
    {
        private readonly ServerConfiguration _config;
        private readonly ObservableCollection<RuleRowViewModel> _allRows = new();
        private readonly ObservableCollection<RuleRowViewModel> _filteredRows = new();
        private string _filterText = string.Empty;

        // Parameterless ctor for the XAML designer.
        public RulesWindow() : this(new ServerConfiguration()) { }

        internal RulesWindow(ServerConfiguration config)
        {
            InitializeComponent();
            _config = config;
            dataGrid.ItemsSource = _filteredRows;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Rebuild();
        }

        private void TxtFilter_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _filterText = (txtFilter.Text ?? string.Empty).Trim();
            ApplyFilter();
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            Rebuild();
        }

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
                    FlattenAppComponents(app, "Blocklist");
            }

            foreach (string name in profile.SpecialExceptions)
            {
                var app = db?.GetApplicationByName(name);
                if (app != null)
                    FlattenAppComponents(app, $"Special: {name.Replace('_', ' ')}");
            }

            foreach (var ex in profile.AppExceptions)
            {
                FlattenException(ex, "User");
            }

            ApplyFilter();
        }

        private void FlattenAppComponents(Application app, string source)
        {
            foreach (var id in app.Components)
            {
                List<ExceptionSubject> foundSubjects;
                try
                {
                    foundSubjects = id.SearchForFile();
                }
                catch
                {
                    foundSubjects = new List<ExceptionSubject>();
                }

                if (foundSubjects.Count == 0 && id.Subject is ExecutableSubject)
                    foundSubjects.Add(id.Subject);

                foreach (var subject in foundSubjects)
                {
                    var ex = id.InstantiateException(subject);
                    FlattenException(ex, source);
                }
            }
        }

        private void FlattenException(FirewallExceptionV3 ex, string source)
        {
            string appPath = string.Empty;
            string serviceName = string.Empty;

            switch (ex.Subject)
            {
                case ServiceSubject srv:
                    appPath = Path.GetFileName(srv.ExecutablePath);
                    serviceName = srv.ServiceName;
                    break;
                case ExecutableSubject exe:
                    appPath = Path.GetFileName(exe.ExecutablePath);
                    break;
                case AppContainerSubject uwp:
                    appPath = !string.IsNullOrEmpty(uwp.DisplayName) ? uwp.DisplayName : uwp.Sid;
                    break;
                case GlobalSubject:
                    appPath = "(all applications)";
                    break;
            }

            switch (ex.Policy)
            {
                case HardBlockPolicy:
                    _allRows.Add(new RuleRowViewModel
                    {
                        App = appPath,
                        Service = serviceName,
                        Action = "Block",
                        Direction = "Any",
                        Protocol = "Any",
                        RemoteAddresses = "*",
                        RemotePorts = "*",
                        LocalPorts = "*",
                        Source = source,
                        RuleName = "Hard block",
                    });
                    break;

                case UnrestrictedPolicy unrestricted:
                    _allRows.Add(new RuleRowViewModel
                    {
                        App = appPath,
                        Service = serviceName,
                        Action = "Allow",
                        Direction = "Any",
                        Protocol = "Any",
                        RemoteAddresses = unrestricted.LocalNetworkOnly ? "LocalSubnet" : "*",
                        RemotePorts = "*",
                        LocalPorts = "*",
                        Source = source,
                        RuleName = "Unrestricted",
                    });
                    break;

                case TcpUdpPolicy tcp:
                    AddTcpUdpRow(appPath, serviceName, source, "TCP Out", tcp.AllowedRemoteTcpConnectPorts);
                    AddTcpUdpRow(appPath, serviceName, source, "UDP Out", tcp.AllowedRemoteUdpConnectPorts);
                    AddTcpUdpRow(appPath, serviceName, source, "TCP Listen", tcp.AllowedLocalTcpListenerPorts);
                    AddTcpUdpRow(appPath, serviceName, source, "UDP Listen", tcp.AllowedLocalUdpListenerPorts);
                    break;

                case RuleListPolicy ruleList:
                    foreach (var r in ruleList.Rules)
                    {
                        _allRows.Add(new RuleRowViewModel
                        {
                            App = appPath,
                            Service = serviceName,
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
                            Source = source,
                            RuleName = r.Name ?? string.Empty,
                        });
                    }
                    break;
            }
        }

        private void AddTcpUdpRow(string app, string service, string source, string label, string? ports)
        {
            if (string.IsNullOrEmpty(ports))
                return;

            bool isListen = label.Contains("Listen");
            _allRows.Add(new RuleRowViewModel
            {
                App = app,
                Service = service,
                Action = "Allow",
                Direction = isListen ? "In" : "Out",
                Protocol = label.StartsWith("TCP") ? "TCP" : "UDP",
                RemoteAddresses = "*",
                RemotePorts = isListen ? "*" : ports,
                LocalPorts = isListen ? ports : "*",
                Source = source,
                RuleName = label,
            });
        }

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

        internal static void ShowRules(ServerConfiguration config)
        {
            var window = new RulesWindow(config);
            window.Show();
        }
    }
}
