using CommunityToolkit.Mvvm.ComponentModel;
using pylorak.TinyWall.DatabaseClasses;

namespace pylorak.TinyWall.ViewModels
{
    public partial class AppFinderItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isChecked;

        public string AppName { get; }

        public Application App { get; }

        public AppFinderItemViewModel(Application app)
        {
            App = app;
            AppName = app.Name;
            _isChecked = app.HasFlag("TWUI:Recommended");
        }
    }
}
