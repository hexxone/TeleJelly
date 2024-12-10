using System.Text.Json.Serialization;

namespace JellyfinPluginHelper;

public struct PluginManifest
{
    [JsonPropertyName("guid")] public string Guid { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("overview")] public string Overview { get; set; }

    [JsonPropertyName("description")] public string Description { get; set; }

    [JsonPropertyName("owner")] public string Owner { get; set; }

    [JsonPropertyName("category")] public string Category { get; set; }

    [JsonPropertyName("imageUrl")] public string ImageUrl { get; set; }

    [JsonPropertyName("versions")] public List<PluginVersion>? Versions { get; set; }
}
