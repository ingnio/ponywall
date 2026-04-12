namespace pylorak.TinyWall.ViewModels
{
    internal class ConnectionRowViewModel
    {
        public string ProcessName { get; set; } = string.Empty;
        public string Services { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public string LocalPort { get; set; } = string.Empty;
        public string LocalAddress { get; set; } = string.Empty;
        public string RemotePort { get; set; } = string.Empty;
        public string RemoteAddress { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;

        // Non-display properties for context menu operations
        public uint Pid { get; set; }
        public string Path { get; set; } = string.Empty;
        public ProcessInfo? ProcessInfo { get; set; }
        public RuleDirection RawDirection { get; set; }
    }
}
