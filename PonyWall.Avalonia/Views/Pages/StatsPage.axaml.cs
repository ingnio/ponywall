using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using pylorak.TinyWall.History;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class StatsPage : UserControl, INavigablePage
    {
        private HistoryReader? _reader;

        public StatsPage()
        {
            InitializeComponent();
        }

        public void Initialize(Controller? controller, ServerConfiguration? config)
        {
            // No controller/config deps needed for stats; reader is created on demand.
        }

        public void OnNavigatedTo()
        {
            try
            {
                if (_reader is null)
                {
                    _reader = new HistoryReader();
                }

                if (!_reader.IsAvailable)
                {
                    txtStatus.Text = "History database not available. Is the service running?";
                    return;
                }

                Reload();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Failed to open history.db: " + ex.Message;
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        public void OnNavigatedFrom()
        {
            // No timers to pause. Dispose the reader so the DB file isn't held open
            // while the user is on another page.
            _reader?.Dispose();
            _reader = null;
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => Reload();

        private void Reload()
        {
            if (_reader is null || !_reader.IsAvailable)
                return;

            try
            {
                txtStatus.Text = "Loading\u2026";
                var vm = StatsViewModel.FromReader(_reader);
                DataContext = vm;
                txtStatus.Text = $"Updated {DateTime.Now:HH:mm:ss}.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Reload failed: " + ex.Message;
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }
    }
}
