using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;

public class QualityProfile
{
    public List<string> PreferredResolutions { get; set; } = ["2160p", "1080p", "720p"];

    public List<ResolutionSettings> MaxFileSizeByResolution { get; set; } = [new() { Resolution = "2160p", Bytes = 50L * 1024 * 1024 * 1024 }, new() { Resolution = "1080p", Bytes = 20L * 1024 * 1024 * 1024 }, new() { Resolution = "720p", Bytes = 10L * 1024 * 1024 * 1024 }];

    public List<ResolutionSettings> MinFileSizeByResolution { get; set; } = [new() { Resolution = "2160p", Bytes = 15L * 1024 * 1024 * 1024 }, new() { Resolution = "1080p", Bytes = 5L * 1024 * 1024 * 1024 }, new() { Resolution = "720p", Bytes = 2L * 1024 * 1024 * 1024 }];

    public List<string> RequiredAudioLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredAudioLanguages { get; set; } = ["ger", "eng"];
    public List<string> RequiredSubtitleLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredSubtitleLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredCodecs { get; set; } = ["H.265", "H.264"];
    public List<string> PreferredAudioCodecs { get; set; } = ["Atmos", "DTS-HD MA", "DTS-HD", "TrueHD", "DDP5.1", "DD5.1", "AAC"];
    public List<string> PreferredHDR { get; set; } = ["Dolby Vision", "HDR10+", "HDR10", "HDR"];
    public List<string> PreferredSources { get; set; } = ["BluRay", "WEB-DL", "WEBRip"];

    public int MinimumSeeders { get; set; } = 3;

    public ScoringWeights Weights { get; set; } = new();
}
