using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class RuleEditorWindow : Window
    {
        private FirewallExceptionV3? _result;

        public RuleEditorWindow()
        {
            InitializeComponent();
        }

        // ================================================================
        // Static show helpers
        // ================================================================

        internal static async Task<FirewallExceptionV3?> ShowCreateDialog(Window owner)
        {
            var dlg = new RuleEditorWindow { Title = "PonyWall — Create Rule" };
            // Default to Allow, Both directions, Any protocol, any addresses/ports
            dlg.txtRemoteAddresses.Text = "*";
            dlg.txtRemotePorts.Text = "*";
            dlg.txtLocalPorts.Text = "*";
            await dlg.ShowDialog(owner);
            return dlg._result;
        }

        internal static async Task<FirewallExceptionV3?> ShowModifyDialog(
            Window owner, RuleRowViewModel row)
        {
            var dlg = new RuleEditorWindow { Title = "PonyWall — Modify Rule" };

            dlg.txtAppPath.Text = row.AppFullPath;
            dlg.txtServiceName.Text = row.Service;
            dlg.cbAction.SelectedIndex = row.Action == "Block" ? 1 : 0;
            dlg.cbDirection.SelectedIndex = row.Direction switch
            {
                "In" => 0,
                "Out" => 1,
                _ => 2, // Both / Any
            };
            dlg.cbProtocol.SelectedIndex = row.Protocol switch
            {
                "TCP" => 1,
                "UDP" => 2,
                "TCP+UDP" => 3,
                "ICMPv4" => 4,
                "ICMPv6" => 5,
                _ => 0, // Any
            };
            dlg.txtRemoteAddresses.Text = row.RemoteAddresses;
            dlg.txtRemotePorts.Text = row.RemotePorts;
            dlg.txtLocalPorts.Text = row.LocalPorts;
            dlg.txtRuleName.Text = row.RuleName;

            await dlg.ShowDialog(owner);
            return dlg._result;
        }

        // ================================================================
        // Event handlers
        // ================================================================

        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var storage = GetTopLevel(this)?.StorageProvider;
                if (storage == null) return;

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select application",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                    txtAppPath.Text = files[0].Path.LocalPath;
            }
            catch (Exception ex)
            {
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void BtnOk_Click(object? sender, RoutedEventArgs e)
        {
            string appPath = (txtAppPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(appPath))
            {
                txtAppPath.Focus();
                return;
            }

            string serviceName = (txtServiceName.Text ?? string.Empty).Trim();
            string action = (cbAction.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Allow";
            string direction = (cbDirection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Both";
            string protocol = (cbProtocol.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Any";
            string remoteAddr = (txtRemoteAddresses.Text ?? string.Empty).Trim();
            string remotePorts = (txtRemotePorts.Text ?? string.Empty).Trim();
            string localPorts = (txtLocalPorts.Text ?? string.Empty).Trim();
            string ruleName = (txtRuleName.Text ?? string.Empty).Trim();

            // Build the subject
            ExceptionSubject subject;
            if (!string.IsNullOrEmpty(serviceName))
                subject = new ServiceSubject(appPath, serviceName);
            else
                subject = new ExecutableSubject(appPath);

            // Build the RuleDef
            var ruleDef = new RuleDef
            {
                Name = string.IsNullOrEmpty(ruleName)
                    ? $"{action} {Path.GetFileName(appPath)}"
                    : ruleName,
                Action = action == "Block" ? RuleAction.Block : RuleAction.Allow,
                Direction = direction switch
                {
                    "In" => RuleDirection.In,
                    "Out" => RuleDirection.Out,
                    _ => RuleDirection.InOut,
                },
                Protocol = protocol switch
                {
                    "TCP" => Protocol.TCP,
                    "UDP" => Protocol.UDP,
                    "TCP+UDP" => Protocol.TcpUdp,
                    "ICMPv4" => Protocol.ICMPv4,
                    "ICMPv6" => Protocol.ICMPv6,
                    _ => Protocol.Any,
                },
                Application = appPath,
                RemoteAddresses = (remoteAddr == "*" || string.IsNullOrEmpty(remoteAddr)) ? null : remoteAddr,
                RemotePorts = (remotePorts == "*" || string.IsNullOrEmpty(remotePorts)) ? null : remotePorts,
                LocalPorts = (localPorts == "*" || string.IsNullOrEmpty(localPorts)) ? null : localPorts,
            };

            if (!string.IsNullOrEmpty(serviceName))
                ruleDef.ServiceName = serviceName;

            var policy = new RuleListPolicy
            {
                Rules = new List<RuleDef> { ruleDef }
            };

            _result = new FirewallExceptionV3(subject, policy);
            Close();
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            _result = null;
            Close();
        }
    }
}
