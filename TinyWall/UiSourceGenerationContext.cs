using System.Text.Json.Serialization;

namespace pylorak.TinyWall
{
    /// <summary>
    /// JSON source-generation context for types that live in the UI assembly
    /// (ControllerSettings and ConfigContainer reference WinForms / Drawing
    /// types and therefore can't sit in TinyWall.Core's SourceGenerationContext).
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ControllerSettings))]
    [JsonSerializable(typeof(ConfigContainer))]
    [JsonSerializable(typeof(DatabaseClasses.SubjectIdentity))]
    [JsonSerializable(typeof(DatabaseClasses.Application))]
    [JsonSerializable(typeof(DatabaseClasses.AppDatabase))]
    internal partial class UiSourceGenerationContext : JsonSerializerContext
    {
    }
}
