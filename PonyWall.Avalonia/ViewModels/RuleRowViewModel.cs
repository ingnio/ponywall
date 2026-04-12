namespace pylorak.TinyWall.ViewModels
{
    /// <summary>
    /// Flat read-only projection of one firewall rule for the Firewall Rules
    /// DataGrid. Built client-side by flattening the active
    /// <see cref="ServerConfiguration"/> + <see cref="DatabaseClasses.AppDatabase"/>
    /// into individual rule rows.
    /// </summary>
    internal sealed class RuleRowViewModel
    {
        public string App { get; init; } = string.Empty;
        public string Service { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string Protocol { get; init; } = string.Empty;
        public string RemoteAddresses { get; init; } = string.Empty;
        public string RemotePorts { get; init; } = string.Empty;
        public string LocalPorts { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string RuleName { get; init; } = string.Empty;
    }
}
