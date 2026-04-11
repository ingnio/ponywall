namespace pylorak.TinyWall.History
{
    /// <summary>
    /// One pending first-block toast notification queued by the service
    /// for the UI to render. Carries the minimal context the UI needs to
    /// build a meaningful Windows toast and to wire up the
    /// Allow once / Allow always / Block always action buttons.
    ///
    /// See Docs/EXPLAINABILITY.md section 13 (First-block toasts).
    /// </summary>
    public sealed class FirstBlockToastInfo
    {
        public string AppPath { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public long TimestampUtcMs { get; set; }
    }
}
