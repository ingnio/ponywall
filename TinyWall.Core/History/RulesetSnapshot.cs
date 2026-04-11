namespace pylorak.TinyWall.History
{
    /// <summary>
    /// A point-in-time snapshot of the serialized ServerConfiguration.
    /// Events reference one of these by foreign key so the explanation
    /// engine can replay the rule match against the historical ruleset.
    /// See Docs/EXPLAINABILITY.md section 4.2.
    /// </summary>
    public sealed record RulesetSnapshot
    {
        public long Id;                       // SQLite auto-increment
        public long TimestampUtcMs;           // when this version became active
        public string ContentHash = string.Empty; // SHA-256 of canonical JSON
        public byte[] ContentJson = System.Array.Empty<byte>(); // the ServerConfiguration at that time
    }
}
