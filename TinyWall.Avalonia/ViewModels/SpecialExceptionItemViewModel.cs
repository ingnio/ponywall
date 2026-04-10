using CommunityToolkit.Mvvm.ComponentModel;

namespace pylorak.TinyWall.ViewModels
{
    public partial class SpecialExceptionItemViewModel : ObservableObject
    {
        public string Id { get; }
        public string Name { get; }

        [ObservableProperty]
        private bool _isChecked;

        public SpecialExceptionItemViewModel(string id, string name, bool isChecked)
        {
            Id = id;
            Name = name;
            _isChecked = isChecked;
        }
    }
}
