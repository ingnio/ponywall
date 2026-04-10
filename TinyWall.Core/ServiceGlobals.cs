using System;
using System.Diagnostics.CodeAnalysis;
using pylorak.TinyWall.DatabaseClasses;

namespace pylorak.TinyWall
{
    internal static class ServiceGlobals
    {
        [AllowNull]
        internal static ServerConfiguration Config;

        internal static Guid ServerChangeset;

        [AllowNull]
        internal static AppDatabase AppDatabase;

        internal static Action<string>? HealthCheckCallback;
    }
}
