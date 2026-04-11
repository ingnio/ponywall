using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Tracks the (app_path -> last_toasted_unix_ms) map used by the
    /// first-block toast feature. Persists to a small JSON file under
    /// %ProgramData%\TinyWall so that long-running apps that have already
    /// been seen don't re-toast every time the service restarts.
    ///
    /// Thread-safe: <see cref="ShouldToast"/> is called from the WFP
    /// callback thread; <see cref="Save"/> is called from a background
    /// flush. All access is guarded by <see cref="_lock"/>.
    /// </summary>
    public sealed class ToastDeduper
    {
        /// <summary>
        /// Default cooldown between consecutive toasts for the same app.
        /// Once we toast for app X, suppress further X toasts for this
        /// many minutes.
        /// </summary>
        public const int DefaultCooldownMinutes = 5;

        private readonly string _path;
        private readonly object _lock = new();
        private readonly Dictionary<string, long> _lastToastedMs = new(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;

        public int CooldownMinutes { get; set; } = DefaultCooldownMinutes;

        public ToastDeduper()
            : this(Path.Combine(Utils.AppDataPath, "toasted-apps.json"))
        {
        }

        public ToastDeduper(string path)
        {
            _path = path;
            Load();
        }

        /// <summary>
        /// Returns true if a first-block toast should be raised for the
        /// given app path right now. If true, the caller MUST then call
        /// <see cref="MarkToasted"/> with the same path so the cooldown
        /// is honored.
        ///
        /// Apps with empty/null paths and the well-known noisy system
        /// hosts (System, svchost.exe) are always rejected.
        /// </summary>
        public bool ShouldToast(string? appPath, long nowUtcMs)
        {
            if (string.IsNullOrEmpty(appPath))
                return false;

            // Mirror the AutoLearnLogEntry filter — these are noisy and
            // not useful as actionable toasts.
            string fileName = TryGetFileName(appPath);
            if (string.Equals(appPath, "System", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(fileName, "svchost.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(fileName, "System", StringComparison.OrdinalIgnoreCase))
                return false;

            long cooldownMs = CooldownMinutes * 60_000L;

            lock (_lock)
            {
                if (_lastToastedMs.TryGetValue(appPath, out long last))
                {
                    if (nowUtcMs - last < cooldownMs)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Records the timestamp at which an app was just toasted.
        /// Marks the deduper dirty so the next <see cref="Save"/> writes
        /// the change to disk.
        /// </summary>
        public void MarkToasted(string appPath, long nowUtcMs)
        {
            if (string.IsNullOrEmpty(appPath))
                return;

            lock (_lock)
            {
                _lastToastedMs[appPath] = nowUtcMs;
                _dirty = true;
            }
        }

        /// <summary>
        /// Number of distinct apps currently in the dedupe map.
        /// Useful for tests + diagnostics.
        /// </summary>
        public int Count
        {
            get { lock (_lock) return _lastToastedMs.Count; }
        }

        /// <summary>
        /// Persists the dedupe map to disk if it has changed since the
        /// last save. Safe to call from a periodic timer.
        /// </summary>
        public void Save()
        {
            Dictionary<string, long>? snapshot = null;
            lock (_lock)
            {
                if (!_dirty) return;
                snapshot = new Dictionary<string, long>(_lastToastedMs);
                _dirty = false;
            }

            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tmp = _path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false }))
                {
                    writer.WriteStartObject();
                    foreach (var kv in snapshot)
                        writer.WriteNumber(kv.Key, kv.Value);
                    writer.WriteEndObject();
                }
                // Best-effort atomic replace.
                if (File.Exists(_path))
                    File.Replace(tmp, _path, null);
                else
                    File.Move(tmp, _path);
            }
            catch
            {
                // We deliberately swallow IO errors here — losing one
                // dedupe save is not worth crashing the service. The
                // dirty flag stays cleared so we don't infinite-retry.
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;

                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var doc = JsonDocument.Parse(fs);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out long ms))
                        _lastToastedMs[prop.Name] = ms;
                }
            }
            catch
            {
                // Corrupt or unreadable file -> start fresh.
                _lastToastedMs.Clear();
            }
        }

        private static string TryGetFileName(string path)
        {
            try { return Path.GetFileName(path); }
            catch { return path; }
        }
    }
}
