using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using pylorak.TinyWall.History;
using pylorak.TinyWall.ViewModels;

namespace pylorak.TinyWall.Views
{
    public partial class StatsWindow : Window
    {
        private HistoryReader? _reader;

        public StatsWindow()
        {
            InitializeComponent();
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
                Reload();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Failed to open history.db: " + ex.Message;
                Utils.LogException(ex, Utils.LOG_ID_GUI);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _reader?.Dispose();
            _reader = null;
            base.OnClosed(e);
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.F5)
                Reload();
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => Reload();

        private void Reload()
        {
            if (_reader is null || !_reader.IsAvailable)
                return;

            try
            {
                txtStatus.Text = "Loading…";
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

        /// <summary>Shows the stats window non-modally.</summary>
        internal static void ShowStats()
        {
            var window = new StatsWindow();
            window.Show();
        }
    }
}
