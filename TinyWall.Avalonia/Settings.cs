using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace pylorak.TinyWall
{
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PKSoft")]
    public sealed class ControllerSettings : ISerializable<ControllerSettings>
    {
        // UI Localization
        [DataMember(EmitDefaultValue = false)]
        public string Language = "auto";

        // Connections window
        [DataMember(EmitDefaultValue = false)]
        public System.Windows.Forms.FormWindowState ConnFormWindowState = System.Windows.Forms.FormWindowState.Normal;
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Point ConnFormWindowLoc = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Size ConnFormWindowSize = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, int> ConnFormColumnWidths = new();
        [DataMember(EmitDefaultValue = false)]
        public bool ConnFormShowConnections = true;
        [DataMember(EmitDefaultValue = false)]
        public bool ConnFormShowOpenPorts = false;
        [DataMember(EmitDefaultValue = false)]
        public bool ConnFormShowBlocked = false;

        // Processes window
        [DataMember(EmitDefaultValue = false)]
        public System.Windows.Forms.FormWindowState ProcessesFormWindowState = System.Windows.Forms.FormWindowState.Normal;
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Point ProcessesFormWindowLoc = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Size ProcessesFormWindowSize = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, int> ProcessesFormColumnWidths = new();

        // Services window
        [DataMember(EmitDefaultValue = false)]
        public System.Windows.Forms.FormWindowState ServicesFormWindowState = System.Windows.Forms.FormWindowState.Normal;
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Point ServicesFormWindowLoc = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Size ServicesFormWindowSize = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, int> ServicesFormColumnWidths = new();

        // UwpPackages window
        [DataMember(EmitDefaultValue = false)]
        public System.Windows.Forms.FormWindowState UwpPackagesFormWindowState = System.Windows.Forms.FormWindowState.Normal;
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Point UwpPackagesFormWindowLoc = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Size UwpPackagesFormWindowSize = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, int> UwpPackagesFormColumnWidths = new();

        // Manage window
        [DataMember(EmitDefaultValue = false)]
        public bool AskForExceptionDetails = false;
        [DataMember(EmitDefaultValue = false)]
        public int SettingsTabIndex;
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Point SettingsFormWindowLoc = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public System.Drawing.Size SettingsFormWindowSize = new(0, 0);
        [DataMember(EmitDefaultValue = false)]
        public Dictionary<string, int> SettingsFormAppListColumnWidths = new();

        // Hotkeys
        [DataMember(EmitDefaultValue = false)]
        public bool EnableGlobalHotkeys = true;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext sc)
        {
            ConnFormColumnWidths ??= new Dictionary<string, int>();
            ProcessesFormColumnWidths ??= new Dictionary<string, int>();
            ServicesFormColumnWidths ??= new Dictionary<string, int>();
            UwpPackagesFormColumnWidths ??= new Dictionary<string, int>();
            SettingsFormAppListColumnWidths ??= new Dictionary<string, int>();
        }

        internal static string UserDataPath
        {
            get
            {
#if DEBUG
                return Path.GetDirectoryName(Utils.ExecutablePath);
#else
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                dir = System.IO.Path.Combine(dir, "TinyWall");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
#endif
            }
        }

        internal static string FilePath => Path.Combine(UserDataPath, "ControllerConfig");

        internal void Save()
        {
            try
            {
                SerializationHelper.SerializeToFile(this, FilePath);
            }
            catch { }
        }

        internal static ControllerSettings Load()
        {
            try
            {
                return SerializationHelper.DeserializeFromFile(FilePath, new ControllerSettings());
            }
            catch { }

            return new ControllerSettings();
        }

        public JsonTypeInfo<ControllerSettings> GetJsonTypeInfo()
        {
            return UiSourceGenerationContext.Default.ControllerSettings;
        }
    }

    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/PKSoft")]
     public sealed class ConfigContainer : ISerializable<ConfigContainer>
    {
        [DataMember(EmitDefaultValue = false)]
        public ServerConfiguration Service;
        [DataMember(EmitDefaultValue = false)]
        public ControllerSettings Controller;

        public ConfigContainer()
        {
            Service = new ServerConfiguration();
            Controller = new ControllerSettings();
        }

        public ConfigContainer(ServerConfiguration server, ControllerSettings client)
        {
            Service = server;
            Controller = client;
        }

        public JsonTypeInfo<ConfigContainer> GetJsonTypeInfo()
        {
            return UiSourceGenerationContext.Default.ConfigContainer;
        }
    }

    internal static class ActiveConfig
    {
        internal static ServerConfiguration? Service
        {
            get => ServiceGlobals.Config;
            set => ServiceGlobals.Config = value;
        }
        [AllowNull]
        internal static ControllerSettings Controller = null;
    }
}


/*
private static bool GetRegistryValueBool(string path, string value, bool standard)
{
    try
    {
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path, RegistryKeyPermissionCheck.ReadSubTree))
        {
            return ((int)key.GetValue(value, standard ? 1 : 0)) != 0;
        }
    }
    catch
    {
        return standard;
    }
}

private void SaveRegistryValueBool(string path, string valueName, bool value)
{
    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree))
    {
        key.SetValue(valueName, value ? 1 : 0);
    }
}*/

