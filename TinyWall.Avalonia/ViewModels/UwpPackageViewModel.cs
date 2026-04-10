namespace pylorak.TinyWall.ViewModels
{
    public class UwpPackageViewModel
    {
        public string Name { get; }
        public string Publisher { get; }
        public UwpPackageList.Package Package { get; }

        public UwpPackageViewModel(UwpPackageList.Package package)
        {
            Package = package;
            Name = package.Name;
            Publisher = package.PublisherId + ", " + package.Publisher;
        }
    }
}
