using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using pylorak.Windows;

namespace pylorak.TinyWall
{
    internal static class ExtensionMethods
    {
        internal static void Append(this StringBuilder sb, ReadOnlySpan<char> str)
        {
            for (int i = 0; i < str.Length; ++i)
                sb.Append(str[i]);
        }
    }

    internal static class Utils
    {
        [SuppressUnmanagedCodeSecurity]
        internal static class SafeNativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsImmersiveProcess(IntPtr hProcess);

            [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
            internal static extern uint DnsFlushResolverCache();

            [DllImport("User32.dll", SetLastError = true)]
            internal static extern int GetSystemMetrics(int nIndex);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out ulong ClientProcessId);

            [DllImport("Wer.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
            internal static extern void WerAddExcludedApplication(
                [MarshalAs(UnmanagedType.LPWStr)] string pwzExeName,
                [MarshalAs(UnmanagedType.Bool)] bool bAllUsers);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.U4)]
            internal static extern int GetLongPathName(
                [MarshalAs(UnmanagedType.LPWStr)] string lpszShortPath,
                [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lpszLongPath,
                [MarshalAs(UnmanagedType.U4)] int cchBuffer);
        }

        private static readonly Random _rng = new();

        // On .NET Core / .NET 5+ Assembly.GetEntryAssembly().Location returns the
        // managed .dll path, not the host .exe. Environment.ProcessPath returns the
        // actual launched executable, which is what SCM and shell-out paths need.
        public static string ExecutablePath { get; } = Environment.ProcessPath
            ?? System.Reflection.Assembly.GetEntryAssembly()!.Location;

        /// <summary>
        /// Path to the service host executable. Set by each entry point on startup.
        /// The UI exe sets this to the sibling TinyWallService.exe; the service exe
        /// sets it to itself.
        /// </summary>
        public static string ServiceExecutablePath { get; set; } = ExecutablePath;

        public static string HexEncode(byte[] binstr)
        {
            var sb = new StringBuilder();
            foreach (byte oct in binstr)
                sb.Append(oct.ToString(@"X2", CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        public static T OnlyFirst<T>(IEnumerable<T> items)
        {
            using IEnumerator<T> iter = items.GetEnumerator();
            iter.MoveNext();
            return iter.Current;
        }

        /// <summary>
        /// Returns the correctly cased version of a local file or directory path. Returns the input path on error.
        /// </summary>
        public static string? GetExactPath(string? path)
        {
            try
            {
                if (!(Directory.Exists(path) || File.Exists(path)))
                    return path;

                var dir = new DirectoryInfo(path);
                var parent = dir.Parent;
                var result = string.Empty;

                while (parent != null)
                {
                    result = Path.Combine(OnlyFirst(parent.EnumerateFileSystemInfos(dir.Name)).Name, result);
                    dir = parent;
                    parent = parent.Parent;
                }

                string root = dir.FullName;
                if (root.Contains(":"))
                {
                    root = root.ToUpperInvariant();
                    result = Path.Combine(root, result);
                    return result;
                }
                else
                {
                    return path;
                }
            }
            catch
            {
                return path;
            }
        }

        internal static bool IsSystemShuttingDown()
        {
            const int SM_SHUTTINGDOWN = 0x2000;
            return 0 != SafeNativeMethods.GetSystemMetrics(SM_SHUTTINGDOWN);
        }

        internal static uint GetForegroundProcessPid()
        {
            IntPtr hwnd = SafeNativeMethods.GetForegroundWindow();
            _ = SafeNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }

        internal static string ProgramFilesx86()
        {
            if ((8 == IntPtr.Size) || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
                return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            else
                return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        internal static void CompressDeflate(string inputFile, string outputFile)
        {
            using var inFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read);
            using var outFile = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
            using var compressedOutFile = new DeflateStream(outFile, CompressionMode.Compress, true);

            byte[] buffer = new byte[4096];
            int numRead;
            while ((numRead = inFile.Read(buffer, 0, buffer.Length)) != 0)
            {
                compressedOutFile.Write(buffer, 0, numRead);
            }
        }

        internal static void DecompressDeflate(string inputFile, string outputFile)
        {
            using var outFile = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
            using var inFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read);
            using var decompressedInFile = new DeflateStream(inFile, CompressionMode.Decompress, true);

            byte[] buffer = new byte[4096];
            int numRead;
            while ((numRead = decompressedInFile.Read(buffer, 0, buffer.Length)) != 0)
            {
                outFile.Write(buffer, 0, numRead);
            }
        }

        internal static string GetPathOfProcessUseTwService(uint pid, Controller controller)
        {
            // Shortcut for special case
            if ((pid == 0) || (pid == 4))
                return "System";

            string ret = GetLongPathName(ProcessManager.GetProcessPath(pid));
            if (string.IsNullOrEmpty(ret))
                ret = controller.TryGetProcessPath(pid);

            return ret;
        }

        internal static string GetPathOfProcess(uint pid)
        {
            // Shortcut for special case
            if ((pid == 0) || (pid == 4))
                return "System";

            return GetLongPathName(ProcessManager.GetProcessPath(pid));
        }

        /// <summary>
        /// Converts a short path to a long path.
        /// </summary>
        internal static string GetLongPathName(string? shortPath)
        {
            if (string.IsNullOrEmpty(shortPath))
                return string.Empty;

            var builder = new StringBuilder(255);
            int result = SafeNativeMethods.GetLongPathName(shortPath, builder, builder.Capacity);
            if ((result > 0) && (result < builder.Capacity))
            {
                return builder.ToString(0, result);
            }
            else
            {
                if (result > 0)
                {
                    builder = new StringBuilder(result);
                    result = SafeNativeMethods.GetLongPathName(shortPath, builder, builder.Capacity);
                    return builder.ToString(0, result);
                }
                else
                {
                    return shortPath;
                }
            }
        }

        internal static string RandomString(int length)
        {
            const string chars = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            char[] buffer = new char[length];
            for (int i = 0; i < length; i++)
            {
                buffer[i] = chars[_rng.Next(chars.Length)];
            }
            return new string(buffer);
        }

        internal static T DeepClone<T>(T obj) where T : ISerializable<T>
        {
            return SerializationHelper.Deserialize(SerializationHelper.Serialize(obj), obj);
        }

        internal static bool StringArrayContains(string[] arr, string val, StringComparison opts = StringComparison.Ordinal)
        {
            for (int i = 0; i < arr.Length; ++i)
            {
                if (string.Equals(arr[i], val, opts))
                    return true;
            }
            return false;
        }

        internal static Process StartProcess(string path, string args, bool asAdmin, bool hideWindow = false)
        {
            var psi = new ProcessStartInfo(path, args);
            psi.WorkingDirectory = Path.GetDirectoryName(path);
            if (asAdmin)
            {
                psi.Verb = "runas";
                psi.UseShellExecute = true;
            }
            if (hideWindow)
                psi.WindowStyle = ProcessWindowStyle.Hidden;

            return Process.Start(psi);
        }

        internal static bool RunningAsAdmin()
        {
            using var wi = WindowsIdentity.GetCurrent();
            var wp = new WindowsPrincipal(wi);
            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static void Invoke(SynchronizationContext syncCtx, SendOrPostCallback method)
        {
            syncCtx?.Send(method, null);
        }

        internal static void SplitFirstLine(string str, out string firstLine, out string restLines)
        {
            string[] lines = str.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);

            firstLine = lines[0];
            restLines = string.Empty;

            if (lines.Length > 1)
            {
                restLines = lines[1];
                for (int i = 2; i < lines.Length; ++i)
                    restLines += Environment.NewLine + lines[i];
            }
        }

        internal static int GetRandomNumber()
        {
            return _rng.Next(0, int.MaxValue);
        }

        internal static Version TinyWallVersion { get; } = typeof(Utils).Assembly.GetName().Version;

        internal static readonly string LOG_ID_SERVICE = "service";
        internal static readonly string LOG_ID_GUI = "gui";
        internal static readonly string LOG_ID_INSTALLER = "installer";

        internal static void LogException(Exception e, string logname)
        {
            TinyWallLog.Logger(logname).LogError(e,
                "TinyWall version: {Version}, Windows version: {Windows}",
                TinyWallVersion, VersionInfo.WindowsVersionString);
        }

        internal static void Log(string info, string logname)
        {
            TinyWallLog.Logger(logname).LogInformation("{Info}", info);
        }

        internal static void FlushDnsCache()
        {
            _ = SafeNativeMethods.DnsFlushResolverCache();
        }

        internal static string AppDataPath
        {
            get
            {
#if DEBUG
                return Path.GetDirectoryName(Utils.ExecutablePath);
#else
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinyWall");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
#endif
            }
        }

        public static bool EqualsCaseInsensitive(string a, string b)
        {
            if (a == b)
                return true;

            return (a != null) && a.Equals(b, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
