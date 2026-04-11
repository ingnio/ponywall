using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace pylorak.TinyWall
{
    [DataContract(Namespace = "TinyWall")]
    public class UpdateModule
    {
        [DataMember, AllowNull]
        public string Component;
        [DataMember]
        public string? ComponentVersion;
        [DataMember]
        public string? DownloadHash;
        [DataMember]
        public string? UpdateURL;
    }

    [DataContract(Namespace = "TinyWall")]
    public class UpdateDescriptor : ISerializable<UpdateDescriptor>
    {
        [DataMember]
        public string MagicWord = "TinyWall Update Descriptor";
        [DataMember]
        public UpdateModule[] Modules = Array.Empty<UpdateModule>();

        public JsonTypeInfo<UpdateDescriptor> GetJsonTypeInfo()
        {
            return SourceGenerationContext.Default.UpdateDescriptor;
        }
    }

    public class ServerState
    {
        public bool HasPassword = false;
        public bool Locked = false;
        public UpdateDescriptor? Update = null;
        public FirewallMode Mode = FirewallMode.Unknown;
        public List<MessageType> ClientNotifs = new();

        /// <summary>
        /// First-block toasts the service has queued for the UI to render.
        /// Drained on every successful GET_SETTINGS poll. See
        /// Docs/EXPLAINABILITY.md section 13.
        /// </summary>
        public List<History.FirstBlockToastInfo> PendingToasts = new();
    }
}
