using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using pylorak.TinyWall.DatabaseClasses;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class AppFinderWindow : Window
    {
        private Thread? _searcherThread;
        private volatile bool _runSearch;

        private readonly ObservableCollection<AppFinderItemViewModel> _items = new();
        private readonly SearchResults _searchResult = new();

        internal List<FirewallExceptionV3> SelectedExceptions { get; } = new();

        public AppFinderWindow()
        {
            InitializeComponent();
            listApps.ItemsSource = _items;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            // Auto-start detection when the window opens
            StartDetection();
        }

        private void BtnStartDetection_Click(object? sender, RoutedEventArgs e)
        {
            if (!_runSearch)
            {
                StartDetection();
            }
            else
            {
                btnStartDetection.IsEnabled = false;
                _runSearch = false;
            }
        }

        private void StartDetection()
        {
            btnStartDetection.Content = "Stop";
            _items.Clear();
            _searchResult.Clear();

            _runSearch = true;
            _searcherThread = new Thread(SearcherWorkerMethod)
            {
                Name = "AppFinder",
                IsBackground = true
            };
            _searcherThread.Start();
        }

        private DateTime _lastStatusUpdate = DateTime.Now;

        private void SearcherWorkerMethod()
        {
            // ------------------------------------
            //       First, do a fast search
            // ------------------------------------
            foreach (Application app in ServiceGlobals.AppDatabase.KnownApplications)
            {
                if (!_runSearch)
                    break;

                if (app.HasFlag("TWUI:Special"))
                    continue;

                foreach (SubjectIdentity id in app.Components)
                {
                    List<ExceptionSubject> subjects = id.SearchForFile();
                    foreach (var subject in subjects)
                    {
                        if (subject is ExecutableSubject exe)
                        {
                            _searchResult.AddEntry(app, exe);
                            Dispatcher.UIThread.Post(() =>
                            {
                                AddRecognizedAppToList(app);
                            });
                        }
                    }
                }
            }

            // ------------------------------------
            //      And now do a slow search
            // ------------------------------------

            // List of all possible paths to search
            string[] searchPaths = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Utils.ProgramFilesx86(),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            // Make sure we do not search the same path twice
            searchPaths = searchPaths.Distinct().ToArray();

            // Construct a list of all file extensions we are looking for
            var exts = new HashSet<string>();
            foreach (Application app in ServiceGlobals.AppDatabase.KnownApplications)
            {
                foreach (SubjectIdentity subjTemplate in app.Components)
                {
                    if (subjTemplate.Subject is ExecutableSubject exesub)
                    {
                        string extFilter = "*" + Path.GetExtension(exesub.ExecutableName).ToUpperInvariant();
                        if (extFilter != "*")
                            exts.Add(extFilter);
                    }
                }
            }

            // Perform search for each path
            foreach (string path in searchPaths)
            {
                if (!_runSearch)
                    break;

                DoSearchPath(path, exts, ServiceGlobals.AppDatabase);
            }

            // Update status when done
            _runSearch = false;
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        lblStatus.Text = "Search complete. Select the applications you want to whitelist.";
                        btnStartDetection.Content = "Start";
                        btnStartDetection.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        // Ignore if the window was already closed
                        Utils.LogException(ex, Utils.LOG_ID_GUI);
                    }
                });
            }
            catch (Exception ex)
            {
                // Thread may be interrupted during shutdown
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void DoSearchPath(string path, HashSet<string> exts, AppDatabase db)
        {
            // Update user feedback periodically
            DateTime now = DateTime.Now;
            if (now - _lastStatusUpdate > TimeSpan.FromMilliseconds(500))
            {
                _lastStatusUpdate = now;
                Dispatcher.UIThread.Post(() =>
                {
                    lblStatus.Text = string.Format(CultureInfo.CurrentCulture, "Searching: {0}", path);
                });
            }

            try
            {
                // Inspect all interesting extensions in the current directory
                foreach (string extFilter in exts)
                {
                    string[] files = Directory.GetFiles(path, extFilter, SearchOption.TopDirectoryOnly);
                    foreach (string file in files)
                    {
                        if (!_runSearch)
                            break;

                        ExecutableSubject subject = (ExecutableSubject)ExceptionSubject.Construct(file, null);
                        Application? app = db.TryGetApp(subject, out FirewallExceptionV3? _, false);
                        if (app != null && (!subject.IsSigned || subject.CertValid))
                        {
                            _searchResult.AddEntry(app, subject);

                            Dispatcher.UIThread.Post(() =>
                            {
                                AddRecognizedAppToList(app);
                            });
                        }
                    }
                }

                // Recurse into subdirectories
                string[] dirs = Directory.GetDirectories(path);
                foreach (string dir in dirs)
                {
                    if (!_runSearch)
                        break;

                    DoSearchPath(dir, exts, db);
                }
            }
            catch (Exception ex)
            {
                // Ignore access-denied and other I/O errors
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        private void AddRecognizedAppToList(Application app)
        {
            // Deduplicate: don't add the same app name twice
            foreach (var item in _items)
            {
                if (item.AppName.Equals(app.Name, StringComparison.Ordinal))
                    return;
            }

            _items.Add(new AppFinderItemViewModel(app));
        }

        private void WaitForThread()
        {
            _runSearch = false;
            _searcherThread?.Join();
        }

        private void BtnSelectAll_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsChecked = true;
        }

        private void BtnDeselectAll_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsChecked = false;
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            WaitForThread();

            foreach (var item in _items)
            {
                if (!item.IsChecked)
                    continue;

                var appFoundFiles = _searchResult.GetFoundComponents(item.App);
                foreach (ExecutableSubject subject in appFoundFiles)
                {
                    Application? app = ServiceGlobals.AppDatabase.TryGetApp(subject, out FirewallExceptionV3? fwex, false);
                    if (fwex != null && app != null && (!subject.IsSigned || subject.CertValid))
                    {
                        SelectedExceptions.Add(fwex);
                    }
                }
            }

            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            WaitForThread();
            Close(false);
        }

        /// <summary>
        /// Shows the AppFinder window and returns the selected firewall exceptions.
        /// Returns an empty list if the user cancels.
        /// </summary>
        internal static async Task<List<FirewallExceptionV3>> DetectApps()
        {
            var tcs = new TaskCompletionSource<List<FirewallExceptionV3>>();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new AppFinderWindow();
                window.Closed += (_, _) =>
                {
                    tcs.TrySetResult(window.SelectedExceptions.Count > 0
                        ? window.SelectedExceptions
                        : new List<FirewallExceptionV3>());
                };
                window.Show();
            });

            return await tcs.Task;
        }

        // ----- Inner class for tracking search results -----

        private sealed class SearchResults
        {
            private readonly Dictionary<Application, List<ExecutableSubject>> _list = new();

            public void Clear()
            {
                _list.Clear();
            }

            public void AddEntry(Application app, ExecutableSubject resolvedSubject)
            {
                if (!_list.ContainsKey(app))
                    _list.Add(app, new List<ExecutableSubject>());

                var subjList = _list[app];
                foreach (var subj in subjList)
                {
                    if (subj.Equals(resolvedSubject))
                        return; // Duplicate
                }

                subjList.Add(resolvedSubject);
            }

            public List<ExecutableSubject> GetFoundComponents(Application app)
            {
                if (_list.TryGetValue(app, out var list))
                    return list;
                return new List<ExecutableSubject>();
            }
        }
    }
}
