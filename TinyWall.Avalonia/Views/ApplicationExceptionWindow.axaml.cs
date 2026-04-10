using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using pylorak.Windows;

namespace pylorak.TinyWall.Views
{
    public partial class ApplicationExceptionWindow : Window
    {
        private static readonly char[] PORT_LIST_SEPARATORS = new char[] { ',' };

        private List<FirewallExceptionV3> TmpExceptionSettings = new();
        private readonly bool PreserveSettingsOnSubjectChange;

        // Timer item for ComboBox
        private sealed class TimerItem
        {
            public string Display { get; }
            public AppExceptionTimer Value { get; }
            public TimerItem(string display, AppExceptionTimer value) { Display = display; Value = value; }
            public override string ToString() => Display;
        }

        internal List<FirewallExceptionV3> ExceptionSettings => TmpExceptionSettings;

        internal ApplicationExceptionWindow(FirewallExceptionV3 fwex, bool preserveSettingsOnSubjectChange = false)
        {
            InitializeComponent();

            PreserveSettingsOnSubjectChange = preserveSettingsOnSubjectChange;
            TmpExceptionSettings.Add(fwex);

            InitTimerCombo();
        }

        // Parameterless constructor for Avalonia designer
        public ApplicationExceptionWindow()
            : this(new FirewallExceptionV3(GlobalSubject.Instance, new UnrestrictedPolicy()))
        {
        }

        private void InitTimerCombo()
        {
            var items = new List<TimerItem>
            {
                new(pylorak.TinyWall.Resources.Messages.Permanent, AppExceptionTimer.Permanent),
                new(pylorak.TinyWall.Resources.Messages.UntilReboot, AppExceptionTimer.Until_Reboot),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XMinutes, 5), AppExceptionTimer.For_5_Minutes),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XMinutes, 30), AppExceptionTimer.For_30_Minutes),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XHour, 1), AppExceptionTimer.For_1_Hour),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XHours, 4), AppExceptionTimer.For_4_Hours),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XHours, 9), AppExceptionTimer.For_9_Hours),
                new(string.Format(CultureInfo.CurrentCulture, pylorak.TinyWall.Resources.Messages.XHours, 24), AppExceptionTimer.For_24_Hours),
            };
            cmbTimer.ItemsSource = items;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            UpdateUI();
        }

        private void UpdateUI()
        {
            // Select timer
            if (cmbTimer.ItemsSource is List<TimerItem> timerItems)
            {
                for (int i = 0; i < timerItems.Count; ++i)
                {
                    if (timerItems[i].Value == TmpExceptionSettings[0].Timer)
                    {
                        cmbTimer.SelectedIndex = i;
                        break;
                    }
                }
            }

            var exeSubj = TmpExceptionSettings[0].Subject as ExecutableSubject;
            var srvSubj = TmpExceptionSettings[0].Subject as ServiceSubject;
            var uwpSubj = TmpExceptionSettings[0].Subject as AppContainerSubject;

            // Update top colored banner
            bool hasSignature = false;
            bool validSignature = false;
            if (exeSubj != null)
            {
                hasSignature = exeSubj.IsSigned;
                validSignature = exeSubj.CertValid;
            }
            else if (uwpSubj != null)
            {
                var packageList = new UwpPackageList();
                var package = packageList.FindPackage(uwpSubj.Sid);
                if (package.HasValue && (package.Value.Tampered != UwpPackageList.TamperedState.Unknown))
                {
                    hasSignature = true;
                    validSignature = (package.Value.Tampered == UwpPackageList.TamperedState.No);
                }
            }

            if (hasSignature && validSignature)
            {
                bannerBorder.Background = new SolidColorBrush(Color.Parse("#90EE90"));
                txtBanner.Text = string.Format(CultureInfo.InvariantCulture,
                    pylorak.TinyWall.Resources.Messages.RecognizedApplication,
                    TmpExceptionSettings[0].Subject.ToString());
            }
            else if (hasSignature && !validSignature)
            {
                bannerBorder.Background = new SolidColorBrush(Color.Parse("#FFB6C1"));
                txtBanner.Text = string.Format(CultureInfo.InvariantCulture,
                    pylorak.TinyWall.Resources.Messages.CompromisedApplication,
                    TmpExceptionSettings[0].Subject.ToString());
            }
            else
            {
                bannerBorder.Background = new SolidColorBrush(Color.Parse("#ADD8E6"));
                txtBanner.Text = pylorak.TinyWall.Resources.Messages.UnknownApplication;
            }

            // Update subject fields
            switch (TmpExceptionSettings[0].Subject.SubjectType)
            {
                case SubjectType.Global:
                    txtAppPath.Text = pylorak.TinyWall.Resources.Messages.AllApplications;
                    txtSrvName.Text = pylorak.TinyWall.Resources.Messages.SubjectTypeGlobal;
                    break;
                case SubjectType.Executable:
                    txtAppPath.Text = exeSubj!.ExecutablePath;
                    txtSrvName.Text = pylorak.TinyWall.Resources.Messages.SubjectTypeExecutable;
                    break;
                case SubjectType.Service:
                    txtAppPath.Text = srvSubj!.ServiceName + " (" + srvSubj.ExecutablePath + ")";
                    txtSrvName.Text = pylorak.TinyWall.Resources.Messages.SubjectTypeService;
                    break;
                case SubjectType.AppContainer:
                    txtAppPath.Text = uwpSubj!.DisplayName;
                    txtSrvName.Text = pylorak.TinyWall.Resources.Messages.SubjectTypeUwpApp;
                    break;
                default:
                    throw new NotImplementedException();
            }

            // Update rule/policy fields
            chkInheritToChildren.IsChecked = TmpExceptionSettings[0].ChildProcessesInherit;

            switch (TmpExceptionSettings[0].Policy.PolicyType)
            {
                case PolicyType.HardBlock:
                    radBlock.IsChecked = true;
                    ApplyPolicyRadioState();
                    break;
                case PolicyType.RuleList:
                    radBlock.IsEnabled = false;
                    radUnrestricted.IsEnabled = false;
                    radTcpUdpUnrestricted.IsEnabled = false;
                    radTcpUdpOut.IsEnabled = false;
                    radOnlySpecifiedPorts.IsEnabled = false;
                    chkRestrictToLocalNetwork.IsEnabled = false;
                    chkRestrictToLocalNetwork.IsChecked = false;
                    break;
                case PolicyType.TcpUdpOnly:
                    TcpUdpPolicy pol = (TcpUdpPolicy)TmpExceptionSettings[0].Policy;
                    if (
                        string.Equals(pol.AllowedLocalTcpListenerPorts, "*")
                        && string.Equals(pol.AllowedLocalUdpListenerPorts, "*")
                        && string.Equals(pol.AllowedRemoteTcpConnectPorts, "*")
                        && string.Equals(pol.AllowedRemoteUdpConnectPorts, "*")
                    )
                    {
                        radTcpUdpUnrestricted.IsChecked = true;
                    }
                    else if (
                        string.Equals(pol.AllowedRemoteTcpConnectPorts, "*")
                        && string.Equals(pol.AllowedRemoteUdpConnectPorts, "*")
                    )
                    {
                        radTcpUdpOut.IsChecked = true;
                    }
                    else
                    {
                        radOnlySpecifiedPorts.IsChecked = true;
                    }

                    ApplyPolicyRadioState();
                    chkRestrictToLocalNetwork.IsChecked = pol.LocalNetworkOnly;
                    txtOutboundPortTCP.Text = pol.AllowedRemoteTcpConnectPorts?.Replace(",", ", ") ?? string.Empty;
                    txtOutboundPortUDP.Text = pol.AllowedRemoteUdpConnectPorts?.Replace(",", ", ") ?? string.Empty;
                    txtListenPortTCP.Text = pol.AllowedLocalTcpListenerPorts?.Replace(",", ", ") ?? string.Empty;
                    txtListenPortUDP.Text = pol.AllowedLocalUdpListenerPorts?.Replace(",", ", ") ?? string.Empty;
                    break;
                case PolicyType.Unrestricted:
                    UnrestrictedPolicy upol = (UnrestrictedPolicy)TmpExceptionSettings[0].Policy;
                    radUnrestricted.IsChecked = true;
                    ApplyPolicyRadioState();
                    chkRestrictToLocalNetwork.IsChecked = upol.LocalNetworkOnly;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static string CleanupPortsList(string str)
        {
            string res = str;
            res = res.Replace(" ", string.Empty);
            res = res.Replace(';', ',');

            // Remove empty elements
            while (res.Contains(",,"))
                res = res.Replace(",,", ",");

            // Terminate early if nothing left
            if (string.IsNullOrEmpty(res))
                return string.Empty;

            // Check validity
            string[] elems = res.Split(PORT_LIST_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
            res = string.Empty;
            foreach (var e in elems)
            {
                bool isRange = (-1 != e.IndexOf('-'));
                if (isRange)
                {
                    string[] minmax = e.Split('-');
                    ushort x = ushort.Parse(minmax[0], CultureInfo.InvariantCulture);
                    ushort y = ushort.Parse(minmax[1], CultureInfo.InvariantCulture);
                    ushort min = Math.Min(x, y);
                    ushort max = Math.Max(x, y);
                    res = $"{res},{min:D}-{max:D}";
                }
                else
                {
                    if (e.Equals("*"))
                        return "*";

                    ushort x = ushort.Parse(e, CultureInfo.InvariantCulture);
                    res = $"{res},{x:D}";
                }
            }

            // Now we have a ',' at the very start. Remove it.
            res = res.Remove(0, 1);

            return res;
        }

        private void ApplyPolicyRadioState()
        {
            if (radBlock.IsChecked == true)
            {
                portFieldsPanel.IsEnabled = false;
                txtListenPortTCP.Text = string.Empty;
                txtListenPortUDP.Text = string.Empty;
                txtOutboundPortTCP.Text = string.Empty;
                txtOutboundPortUDP.Text = string.Empty;
                chkRestrictToLocalNetwork.IsEnabled = false;
                chkRestrictToLocalNetwork.IsChecked = false;
            }
            else if (radOnlySpecifiedPorts.IsChecked == true)
            {
                portFieldsPanel.IsEnabled = true;
                txtListenPortTCP.Text = string.Empty;
                txtListenPortUDP.Text = string.Empty;
                txtOutboundPortTCP.Text = string.Empty;
                txtOutboundPortUDP.Text = string.Empty;
                txtOutboundPortTCP.IsEnabled = true;
                txtOutboundPortUDP.IsEnabled = true;
                txtListenPortTCP.IsEnabled = true;
                txtListenPortUDP.IsEnabled = true;
                lblListenTCP.IsEnabled = true;
                lblListenUDP.IsEnabled = true;
                chkRestrictToLocalNetwork.IsEnabled = true;
            }
            else if (radTcpUdpOut.IsChecked == true)
            {
                portFieldsPanel.IsEnabled = true;
                txtListenPortTCP.Text = string.Empty;
                txtListenPortUDP.Text = string.Empty;
                txtOutboundPortTCP.Text = "*";
                txtOutboundPortUDP.Text = "*";
                txtOutboundPortTCP.IsEnabled = false;
                txtOutboundPortUDP.IsEnabled = false;
                txtListenPortTCP.IsEnabled = true;
                txtListenPortUDP.IsEnabled = true;
                lblListenTCP.IsEnabled = false;
                lblListenUDP.IsEnabled = false;
                chkRestrictToLocalNetwork.IsEnabled = true;
            }
            else if (radTcpUdpUnrestricted.IsChecked == true)
            {
                portFieldsPanel.IsEnabled = false;
                txtListenPortTCP.Text = "*";
                txtListenPortUDP.Text = "*";
                txtOutboundPortTCP.Text = "*";
                txtOutboundPortUDP.Text = "*";
                chkRestrictToLocalNetwork.IsEnabled = true;
            }
            else if (radUnrestricted.IsChecked == true)
            {
                portFieldsPanel.IsEnabled = false;
                txtListenPortTCP.Text = "*";
                txtListenPortUDP.Text = "*";
                txtOutboundPortTCP.Text = "*";
                txtOutboundPortUDP.Text = "*";
                chkRestrictToLocalNetwork.IsEnabled = true;
            }
        }

        private void RadRestriction_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            // Only respond to the radio that got checked, not the one being unchecked
            if (sender is RadioButton rb && rb.IsChecked == true)
                ApplyPolicyRadioState();
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            TmpExceptionSettings[0].ChildProcessesInherit = chkInheritToChildren.IsChecked == true;

            if (radBlock.IsChecked == true)
            {
                TmpExceptionSettings[0].Policy = HardBlockPolicy.Instance;
            }
            else if (radOnlySpecifiedPorts.IsChecked == true || radTcpUdpOut.IsChecked == true || radTcpUdpUnrestricted.IsChecked == true)
            {
                var pol = new TcpUdpPolicy();

                try
                {
                    pol.LocalNetworkOnly = chkRestrictToLocalNetwork.IsChecked == true;
                    pol.AllowedRemoteTcpConnectPorts = CleanupPortsList(txtOutboundPortTCP.Text ?? string.Empty);
                    pol.AllowedRemoteUdpConnectPorts = CleanupPortsList(txtOutboundPortUDP.Text ?? string.Empty);
                    pol.AllowedLocalTcpListenerPorts = CleanupPortsList(txtListenPortTCP.Text ?? string.Empty);
                    pol.AllowedLocalUdpListenerPorts = CleanupPortsList(txtListenPortUDP.Text ?? string.Empty);
                    TmpExceptionSettings[0].Policy = pol;
                }
                catch
                {
                    NotificationService.Notify(
                        pylorak.TinyWall.Resources.Messages.PortListInvalid,
                        NotificationLevel.Warning);
                    return;
                }
            }
            else if (radUnrestricted.IsChecked == true)
            {
                var pol = new UnrestrictedPolicy();
                pol.LocalNetworkOnly = chkRestrictToLocalNetwork.IsChecked == true;
                TmpExceptionSettings[0].Policy = pol;
            }

            TmpExceptionSettings[0].CreationDate = DateTime.Now;

            // Update timer from combo
            if (cmbTimer.SelectedItem is TimerItem ti)
                TmpExceptionSettings[0].Timer = ti.Value;

            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Application",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Executables (*.exe)") { Patterns = new[] { "*.exe" } },
                    new FilePickerFileType("All Files (*)") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 1)
            {
                string filePath = files[0].Path.LocalPath;
                ReinitFormFromSubject(new ExecutableSubject(
                    PathMapper.Instance.ConvertPathIgnoreErrors(filePath, PathFormat.Win32)));
            }
        }

        private void BtnProcess_Click(object? sender, RoutedEventArgs e)
        {
            NotificationService.Notify("Process selection is not yet implemented in the Avalonia port.");
        }

        private void BtnService_Click(object? sender, RoutedEventArgs e)
        {
            NotificationService.Notify("Service selection is not yet implemented in the Avalonia port.");
        }

        private async void BtnUwpApp_Click(object? sender, RoutedEventArgs e)
        {
            var packageList = await UwpPackagesWindow.ChoosePackage(false);
            if (packageList.Count == 0) return;

            ReinitFormFromSubject(new AppContainerSubject(
                packageList[0].Sid,
                packageList[0].Name,
                packageList[0].Publisher,
                packageList[0].PublisherId));
        }

        private void CmbTimer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TmpExceptionSettings.Count > 0 && cmbTimer.SelectedItem is TimerItem ti)
                TmpExceptionSettings[0].Timer = ti.Value;
        }

        private void ReinitFormFromSubject(ExceptionSubject subject)
        {
            if (PreserveSettingsOnSubjectChange && (TmpExceptionSettings.Count == 1))
            {
                TmpExceptionSettings[0] = Utils.DeepClone(TmpExceptionSettings[0]);
                TmpExceptionSettings[0].RegenerateId();
                TmpExceptionSettings[0].Subject = subject;
            }
            else
            {
                List<FirewallExceptionV3> exceptions = ServiceGlobals.AppDatabase.GetExceptionsForApp(subject, true, out _);
                if (exceptions.Count == 0)
                    return;

                TmpExceptionSettings = exceptions;
            }

            UpdateUI();

            if (TmpExceptionSettings.Count > 1)
            {
                // Multiple known files, just accept them as is
                Close(true);
            }
        }

        /// <summary>
        /// Shows the ApplicationExceptionWindow and returns the exception settings.
        /// Returns null if the user cancels.
        /// </summary>
        internal static async Task<List<FirewallExceptionV3>?> EditException(
            FirewallExceptionV3 fwex,
            bool preserveSettingsOnSubjectChange = false)
        {
            var tcs = new TaskCompletionSource<List<FirewallExceptionV3>?>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new ApplicationExceptionWindow(fwex, preserveSettingsOnSubjectChange);
                window.Closed += (_, _) =>
                {
                    // Check if closed via OK (true) or cancelled
                    // The window passes true/false to Close()
                    tcs.TrySetResult(window.ExceptionSettings.Count > 0
                        ? window.ExceptionSettings
                        : null);
                };
                window.Show();
            });

            return await tcs.Task;
        }
    }
}
