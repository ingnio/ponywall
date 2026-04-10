using System.Text.Json.Serialization;

namespace pylorak.TinyWall
{
    /// <summary>
    /// JSON source-generation context for types that live in the UI assembly
    /// (ControllerSettings and ConfigContainer). Kept separate from
    /// TinyWall.Core's SourceGenerationContext because these types belong to
    /// the controller/UI tier.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ControllerSettings))]
    [JsonSerializable(typeof(ConfigContainer))]
    internal partial class UiSourceGenerationContext : JsonSerializerContext
    {
    }
}
