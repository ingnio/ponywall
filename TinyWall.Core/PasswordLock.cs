using System.IO;
using System.Text;
using pylorak.Utilities;

namespace pylorak.TinyWall
{
    public static class PasswordLock
    {
        internal static string PasswordFilePath { get; } = Path.Combine(Utils.AppDataPath, "pwd");

        private static bool _Locked;

        internal static bool Locked
        {
            get { return _Locked && HasPassword; }
            set
            {
                if (value && HasPassword)
                    _Locked = true;
            }
        }

        internal static void SetPass(string password)
        {
            // Construct file path
            string SettingsFile = PasswordFilePath;

            if (password == string.Empty)
                // If we have no password, delete password explicitly
                File.Delete(SettingsFile);
            else
            {
                using var fileUpdater = new AtomicFileUpdater(PasswordFilePath);
                string salt = Utils.RandomString(8);
                string hash = Pbkdf2.GetHashForStorage(password, salt, 150000, 16);
                File.WriteAllText(fileUpdater.TemporaryFilePath, hash, Encoding.UTF8);
                fileUpdater.Commit();
            }
        }

        internal static bool Unlock(string password)
        {
            if (!HasPassword)
                return true;

            try
            {
                string storedHash = System.IO.File.ReadAllText(PasswordFilePath, System.Text.Encoding.UTF8);
                _Locked = !Pbkdf2.CompareHash(storedHash, password);
            }
            catch { }

            return !_Locked;
        }

        internal static bool HasPassword
        {
            get
            {
                if (!File.Exists(PasswordFilePath))
                    return false;

                var fi = new FileInfo(PasswordFilePath);
                return (fi.Length != 0);
            }
        }
    }
}
