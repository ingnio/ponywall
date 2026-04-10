using System.Linq;

namespace pylorak.TinyWall.ViewModels
{
    internal class ProcessItemViewModel
    {
        public string Name { get; }
        public string Services { get; }
        public string Path { get; }
        public ProcessInfo ProcessInfo { get; }

        public ProcessItemViewModel(ProcessInfo info)
        {
            ProcessInfo = info;
            Name = info.Package.HasValue ? info.Package.Value.Name : System.IO.Path.GetFileNameWithoutExtension(info.Path);
            Services = string.Join(", ", info.Services.OrderBy(s => s));
            Path = info.Path;
        }
    }
}
