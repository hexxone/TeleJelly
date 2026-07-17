namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;

public class ScoringWeights
{
    public int ResolutionPerPosition { get; set; } = 1000;
    public int CodecPerPosition { get; set; } = 100;
    public int HdrPerPosition { get; set; } = 100;
    public int SourcePerPosition { get; set; } = 100;
    public int AudioCodecPerPosition { get; set; } = 80;
    public int PreferredAudioLanguagePerMatch { get; set; } = 50;
    public int PreferredSubtitleLanguagePerMatch { get; set; } = 50;
    public int SeederMultiplier { get; set; } = 10;
    public int MaxSeederBonus { get; set; } = 500;
    public int BitratePerMbps { get; set; } = 8;
    public int MaxBitrateBonus { get; set; } = 250;

    public int RecentReleaseThresholdDays { get; set; } = 30;
    public int ModerateAgeThresholdDays { get; set; } = 90;
    public int OldContentThresholdDays { get; set; } = 365;

    public double RecentReleaseAgeImpact { get; set; } = 0.15;
    public double ModerateAgeImpact { get; set; } = 0.11;
    public double OldContentAgeImpact { get; set; } = 0.08;
    public double ArchivedContentAgeImpact { get; set; } = 0.05;

    public int AbsoluteFreshThreshold1Days { get; set; } = 30;
    public int AbsoluteFreshThreshold2Days { get; set; } = 90;
    public int AbsoluteFreshThreshold3Days { get; set; } = 365;
    public int AbsoluteFreshThreshold4Days { get; set; } = 1095;

    public double AbsoluteFreshnessFactor1 { get; set; } = 1.0;
    public double AbsoluteFreshnessFactor2 { get; set; } = 0.8;
    public double AbsoluteFreshnessFactor3 { get; set; } = 0.5;
    public double AbsoluteFreshnessFactor4 { get; set; } = 0.3;
    public double AbsoluteFreshnessFactor5 { get; set; } = 0.1;
}