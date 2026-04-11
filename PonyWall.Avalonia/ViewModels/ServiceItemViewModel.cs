namespace pylorak.TinyWall.ViewModels
{
    public class ServiceItemViewModel
    {
        public string DisplayName { get; }
        public string ServiceName { get; }
        public string ExecutablePath { get; }

        public ServiceItemViewModel(string displayName, string serviceName, string executablePath)
        {
            DisplayName = displayName;
            ServiceName = serviceName;
            ExecutablePath = executablePath;
        }
    }
}
