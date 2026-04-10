using System.Diagnostics.CodeAnalysis;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Minimal stub for GlobalInstances to satisfy ProcessInfo.cs compilation.
    /// The full WinForms version has icon caching and other UI-specific members.
    /// </summary>
    internal static class GlobalInstances
    {
        [AllowNull]
        internal static Controller Controller;

        public static void InitClient()
        {
            Controller ??= new Controller("TinyWallController");
        }
    }
}
