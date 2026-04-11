using System;
using System.Globalization;
using System.IO;
using System.Net;

namespace pylorak.TinyWall
{
    internal static class UpdateHelper
    {
        private const int UPDATER_VERSION = 6;
        private const string URL_UPDATE_DESCRIPTOR = @"https://tinywall.pados.hu/updates/UpdVer{0}/update.json";

        internal static UpdateDescriptor GetDescriptor()
        {
            var url = string.Format(CultureInfo.InvariantCulture, URL_UPDATE_DESCRIPTOR, UPDATER_VERSION);
            var tmpFile = Path.GetTempFileName();

            try
            {
                using (var HTTPClient = new WebClient())
                {
                    HTTPClient.DownloadFile(url, tmpFile);
                }

                var descriptor = SerializationHelper.DeserializeFromFile(tmpFile, new UpdateDescriptor());
                if (descriptor.MagicWord != "TinyWall Update Descriptor")
                    throw new ApplicationException("Bad update descriptor file.");

                return descriptor;
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        internal static UpdateModule? GetUpdateModule(UpdateDescriptor descriptor, string moduleName)
        {
            for (int i = 0; i < descriptor.Modules.Length; ++i)
            {
                if (descriptor.Modules[i].Component.Equals(moduleName, StringComparison.InvariantCultureIgnoreCase))
                    return descriptor.Modules[i];
            }

            return null;
        }

        internal static UpdateModule? GetMainAppModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "TinyWall");
        }
        internal static UpdateModule? GetHostsFileModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "HostsFile");
        }
        internal static UpdateModule? GetDatabaseFileModule(UpdateDescriptor descriptor)
        {
            return GetUpdateModule(descriptor, "Database");
        }
    }
}
