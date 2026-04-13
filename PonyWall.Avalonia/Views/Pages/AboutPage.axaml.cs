using System.Globalization;
using Avalonia.Controls;

namespace pylorak.TinyWall.Views
{
    public partial class AboutPage : UserControl, INavigablePage
    {
        public AboutPage()
        {
            InitializeComponent();
        }

        public void Initialize(Controller? controller, ServerConfiguration? config) { }

        public void OnNavigatedTo()
        {
            lblVersion.Text = string.Format(CultureInfo.CurrentCulture, "PonyWall {0}",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0");
        }

        public void OnNavigatedFrom() { }
    }
}
