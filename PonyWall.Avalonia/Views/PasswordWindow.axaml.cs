using Avalonia.Controls;
using Avalonia.Interactivity;

namespace pylorak.TinyWall.Views
{
    public partial class PasswordWindow : Window
    {
        /// <summary>
        /// The SHA-256 hash of the entered password, or null if cancelled.
        /// </summary>
        public string? PassHash { get; private set; }

        public PasswordWindow()
        {
            InitializeComponent();
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            txtPassphrase.Focus();
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            PassHash = Hasher.HashString(txtPassphrase.Text ?? string.Empty);
            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            PassHash = null;
            Close(false);
        }
    }
}
