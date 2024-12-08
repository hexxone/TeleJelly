using System.Text.Json.Serialization;

namespace JellyfinPluginHelper;

public struct PluginVersion
{
    [JsonPropertyName("targetAbi")] public string TargetAbi { get; set; }

    [JsonPropertyName("checksum")] public string Checksum { get; set; }

    [JsonPropertyName("sourceUrl")] public string SourceUrl { get; set; }

    [JsonPropertyName("timestamp")] public string Timestamp { get; set; }

    [JsonPropertyName("version")] public string Version { get; set; }

    [JsonPropertyName("changelog")] public string Changelog { get; set; }
}