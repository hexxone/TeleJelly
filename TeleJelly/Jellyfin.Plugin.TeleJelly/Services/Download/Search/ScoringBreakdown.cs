using System;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public sealed class ScoringBreakdown
{
    public string Title { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public bool Disqualified { get; set; }
    public string? DisqualificationReason { get; set; }

    public double BaseScore { get; set; }
    public double ResolutionScore { get; set; }
    public double CodecScore { get; set; }
    public double HdrScore { get; set; }
    public double SourceScore { get; set; }
    public double AudioCodecScore { get; set; }
    public double AudioLanguageScore { get; set; }
    public double SubtitleLanguageScore { get; set; }
    public double BitrateScore { get; set; }
    public double SeederScore { get; set; }
    public double BaseQualityScore { get; set; }

    public double AgeMultiplier { get; set; } = 1.0;
    public double AgeBonus { get; set; }
    public DateTime? UploadedDate { get; set; }
    public double DaysOld { get; set; }
    public double DateSpreadDays { get; set; }
    public double AbsoluteFreshness { get; set; }
    public double BaseAgeImpact { get; set; }
    public double FinalAgeImpact { get; set; }

    public double TotalScore { get; set; }

    public override string ToString()
    {
        if (Disqualified)
        {
            return $"[DISQUALIFIED] {Title} ({ServiceType}): {DisqualificationReason}";
        }

        var baseInfo = $"{Title} ({ServiceType}): Total={TotalScore:F0} [Base={BaseScore:F0}, Res={ResolutionScore:F0}, Codec={CodecScore:F0}, HDR={HdrScore:F0}, Source={SourceScore:F0}, AudioCodec={AudioCodecScore:F0}, Audio={AudioLanguageScore:F0}, Subs={SubtitleLanguageScore:F0}, Bitrate={BitrateScore:F0}, Seeders={SeederScore:F0}]";

        if (AgeMultiplier > 1.0)
        {
            baseInfo += $" | Age: {AgeMultiplier:F3}x (+{AgeBonus:F0}), {DaysOld:F0}d old, Spread={DateSpreadDays:F0}d, Fresh={AbsoluteFreshness:F2}, Impact={FinalAgeImpact:F3}";
        }

        return baseInfo;
    }
}