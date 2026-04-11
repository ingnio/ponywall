using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using pylorak.TinyWall.DatabaseClasses;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Process-wide mutable state shared between the firewall worker threads,
    /// the pipe message handler, and (in the UI process) the polling timer.
    ///
    /// This class is shared between two different process roles:
    ///
    /// - In PonyWallService.exe: holds the server-side state. Config and
    ///   ServerChangeset are mutated by the pipe handler (PUT_SETTINGS) and
    ///   read by the firewall worker thread on every rule application. The
    ///   AppDatabase is loaded once at service startup and read-only after.
    ///   HealthCheckCallback is set once at startup.
    ///
    /// - In PonyWall.exe: holds the client-side cache of last-seen
    ///   server state. Config is updated by the polling timer (UI thread)
    ///   when GetServerConfig returns a new changeset, and read by every UI
    ///   action that needs to inspect server settings. The AppDatabase is
    ///   loaded once at App startup and read-only after.
    ///   HealthCheckCallback is unused.
    ///
    /// **Threading:**
    /// - Config: ref reads/writes are atomic in .NET, but mutation of the
    ///   ServerConfiguration *contents* must always go through "clone +
    ///   mutate clone + assign clone back" to avoid tearing reads.
    ///   ConfigLock provides explicit serialization where needed.
    /// - ServerChangeset: a Guid is 16 bytes; reads/writes are NOT atomic.
    ///   Always touch under ConfigLock.
    /// - AppDatabase: assigned once during startup, read-only after.
    ///   No lock required after the startup assignment.
    /// - HealthCheckCallback: assigned once during startup, read-only after.
    ///
    /// **Future direction (deferred):** Replace this static class with an
    /// IConfigStore / IServerStateStore pair injected via constructor into
    /// TinyWallService and into the Avalonia App. This is a separate
    /// architectural project — see notes in the Avalonia port plan.
    /// </summary>
    internal static class ServiceGlobals
    {
        /// <summary>
        /// Lock object for serialized access to Config + ServerChangeset.
        /// Hold this when reading or writing both fields together.
        /// </summary>
        internal static readonly object ConfigLock = new();

        [AllowNull]
        internal static ServerConfiguration Config;

        internal static Guid ServerChangeset;

        [AllowNull]
        internal static AppDatabase AppDatabase;

        internal static Action<string>? HealthCheckCallback;
    }
}
