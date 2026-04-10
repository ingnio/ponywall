using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace pylorak.TinyWall.ViewModels
{
    public partial class ExceptionListItemViewModel : ObservableObject
    {
        public string Name { get; }
        public string Type { get; }
        public string Path { get; }
        public string Date { get; }
        public FirewallExceptionV3 Exception { get; }
        public IBrush? RowBackground { get; }

        public ExceptionListItemViewModel(FirewallExceptionV3 ex, string name, string type, string path, IBrush? background)
        {
            Exception = ex;
            Name = name;
            Type = type;
            Path = path;
            Date = ex.CreationDate.ToString("yyyy/MM/dd HH:mm");
            RowBackground = background;
        }
    }
}
