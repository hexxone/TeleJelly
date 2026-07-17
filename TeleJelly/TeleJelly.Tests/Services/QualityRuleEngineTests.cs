using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class QualityRuleEngineTests
{
    private readonly QualityRuleEngine _engine = new();

    [Test]
    public void ScoreResult_PrefersBetterAudioCodecAndBitrate()
    {
        var profile = CreateProfile(minimumSeeders: 1);

        var premium = new SearchResult
        {
            Title = "Premium release",
            DownloadLink = "magnet:?xt=urn:btih:premium",
            Resolution = "1080p",
            Codec = "H.265",
            HDR = "HDR10",
            Source = "BluRay",
            AudioCodecs = ["Atmos", "TrueHD"],
            Bitrate = 18000,
            Seeders = 25,
            ServiceType = DownloadServiceType.Torrent,
            UploadedDate = DateTime.UtcNow.AddDays(-1)
        };

        var basic = new SearchResult
        {
            Title = "Basic release",
            DownloadLink = "magnet:?xt=urn:btih:basic",
            Resolution = "1080p",
            Codec = "H.265",
            HDR = "HDR10",
            Source = "BluRay",
            AudioCodecs = ["AAC"],
            Bitrate = 4000,
            Seeders = 25,
            ServiceType = DownloadServiceType.Torrent,
            UploadedDate = DateTime.UtcNow.AddDays(-1)
        };

        var premiumScore = QualityRuleEngine.ScoreResult(premium, profile);
        var basicScore = QualityRuleEngine.ScoreResult(basic, profile);
        var premiumBreakdown = _engine.GetScoringBreakdown(premium, profile, [premium, basic]);
        var basicBreakdown = _engine.GetScoringBreakdown(basic, profile, [premium, basic]);

        TestContext.WriteLine(premiumBreakdown.ToString());
        TestContext.WriteLine(basicBreakdown.ToString());

        Assert.That(premiumScore, Is.GreaterThan(basicScore));
        Assert.That(premiumBreakdown.AudioCodecScore, Is.GreaterThan(basicBreakdown.AudioCodecScore));
        Assert.That(premiumBreakdown.BitrateScore, Is.GreaterThan(basicBreakdown.BitrateScore));
        Assert.That(premiumBreakdown.TotalScore, Is.GreaterThan(basicBreakdown.TotalScore));
    }

    [Test]
    public void ScoreResult_DisqualifiesTorrentBelowMinimumSeeders()
    {
        var result = CreateResult(seeders: 1);
        var profile = CreateProfile(minimumSeeders: 3);

        var score = QualityRuleEngine.ScoreResult(result, profile);
        var breakdown = _engine.GetScoringBreakdown(result, profile);

        Assert.That(score, Is.Zero);
        Assert.That(breakdown.Disqualified, Is.True);
        Assert.That(breakdown.DisqualificationReason, Does.Contain("Insufficient seeders"));
    }

    [Test]
    public void ScoreResult_DisqualifiesWhenRequiredAudioLanguageMissing()
    {
        var result = CreateResult(audioLanguages: ["French"], subtitleLanguages: ["German"]);
        var profile = CreateProfile(requiredAudioLanguages: ["German"], requiredSubtitleLanguages: ["German"]);

        var score = QualityRuleEngine.ScoreResult(result, profile);
        var breakdown = _engine.GetScoringBreakdown(result, profile);

        Assert.That(score, Is.Zero);
        Assert.That(breakdown.Disqualified, Is.True);
        Assert.That(breakdown.DisqualificationReason, Does.Contain("Missing required audio language"));
    }

    [Test]
    public void ScoreResult_MatchesConfiguredLanguageCodesToProviderLanguageNames()
    {
        var result = CreateResult(audioLanguages: ["German", "English"], subtitleLanguages: ["German"]);
        var profile = CreateProfile(requiredAudioLanguages: ["ger", "eng"], requiredSubtitleLanguages: ["ger"]);

        var score = QualityRuleEngine.ScoreResult(result, profile);
        var breakdown = _engine.GetScoringBreakdown(result, profile);

        Assert.That(score, Is.GreaterThan(0));
        Assert.That(breakdown.Disqualified, Is.False);
    }

    [Test]
    public void ScoreResult_DisqualifiesWhenKnownSizeViolatesResolutionBounds()
    {
        var result = CreateResult(fileSizeBytes: 1L * 1024 * 1024 * 1024, resolution: "1080p");
        var profile = CreateProfile(minSizeByResolution:
        [
            new ResolutionSettings { Resolution = "1080p", Bytes = 5L * 1024 * 1024 * 1024 }
        ]);

        var breakdown = _engine.GetScoringBreakdown(result, profile);

        Assert.That(breakdown.Disqualified, Is.True);
        Assert.That(breakdown.DisqualificationReason, Does.Contain("File too small"));
    }

    [Test]
    public void GetScoringBreakdown_TotalMatchesComponentSumWhenNoAgeBonusApplies()
    {
        var result = CreateResult(
            resolution: "2160p",
            codec: "H.265",
            hdr: "HDR10",
            source: "BluRay",
            audioLanguages: ["German", "English"],
            subtitleLanguages: ["German"],
            audioCodecs: ["Atmos", "TrueHD"],
            bitrate: 18000,
            seeders: 45);

        var profile = CreateProfile(minimumSeeders: 1);
        var breakdown = _engine.GetScoringBreakdown(result, profile, [result]);

        var expectedTotal = breakdown.BaseScore +
                            breakdown.ResolutionScore +
                            breakdown.CodecScore +
                            breakdown.HdrScore +
                            breakdown.SourceScore +
                            breakdown.AudioCodecScore +
                            breakdown.AudioLanguageScore +
                            breakdown.SubtitleLanguageScore +
                            breakdown.BitrateScore +
                            breakdown.SeederScore;

        TestContext.WriteLine(breakdown.ToString());

        Assert.That(breakdown.Disqualified, Is.False);
        Assert.That(breakdown.AgeMultiplier, Is.EqualTo(1d));
        Assert.That(breakdown.TotalScore, Is.EqualTo(expectedTotal).Within(0.001d));
    }

    [Test]
    public void GetScoringBreakdown_PrefersNewerReleaseWhenBaseQualityIsEqual()
    {
        var newer = CreateResult(uploadedDate: DateTime.UtcNow.AddDays(-1), seeders: 20);
        var older = CreateResult(uploadedDate: DateTime.UtcNow.AddDays(-25), seeders: 20, downloadLink: "magnet:?xt=urn:btih:older");
        var profile = CreateProfile(minimumSeeders: 1);

        var newerBreakdown = _engine.GetScoringBreakdown(newer, profile, [newer, older]);
        var olderBreakdown = _engine.GetScoringBreakdown(older, profile, [newer, older]);

        TestContext.WriteLine(newerBreakdown.ToString());
        TestContext.WriteLine(olderBreakdown.ToString());

        Assert.That(newerBreakdown.TotalScore, Is.GreaterThan(olderBreakdown.TotalScore));
        Assert.That(newerBreakdown.AgeMultiplier, Is.GreaterThan(olderBreakdown.AgeMultiplier));
        Assert.That(newerBreakdown.FinalAgeImpact, Is.GreaterThan(0d));
    }

    [Test]
    public void GetScoringBreakdown_WithNoDateInformationLeavesAgeNeutral()
    {
        var left = CreateResult(downloadLink: "magnet:?xt=urn:btih:left", uploadedDate: null);
        var right = CreateResult(downloadLink: "magnet:?xt=urn:btih:right", uploadedDate: null);
        var profile = CreateProfile(minimumSeeders: 1);

        var breakdown = _engine.GetScoringBreakdown(left, profile, [left, right]);

        Assert.That(breakdown.AgeMultiplier, Is.EqualTo(1d));
        Assert.That(breakdown.AgeBonus, Is.Zero);
        Assert.That(breakdown.UploadedDate, Is.Null);
    }

    private static SearchResult CreateResult(
        int seeders = 10,
        long fileSizeBytes = 8L * 1024 * 1024 * 1024,
        string resolution = "1080p",
        string codec = "H.265",
        string? hdr = "HDR10",
        string source = "WEB-DL",
        string[]? audioLanguages = null,
        string[]? subtitleLanguages = null,
        string[]? audioCodecs = null,
        int? bitrate = 8000,
        DateTime? uploadedDate = null,
        string downloadLink = "magnet:?xt=urn:btih:test")
    {
        return new SearchResult
        {
            Title = "Example Release",
            DownloadLink = downloadLink,
            Resolution = resolution,
            Codec = codec,
            HDR = hdr,
            Source = source,
            FileSizeBytes = fileSizeBytes,
            Seeders = seeders,
            ServiceType = DownloadServiceType.Torrent,
            AudioLanguages = audioLanguages ?? ["German", "English"],
            SubtitleLanguages = subtitleLanguages ?? ["German", "English"],
            AudioCodecs = audioCodecs ?? ["DDP5.1"],
            Bitrate = bitrate,
            UploadedDate = uploadedDate
        };
    }

    private static QualityProfile CreateProfile(
        int minimumSeeders = 1,
        IEnumerable<string>? requiredAudioLanguages = null,
        IEnumerable<string>? requiredSubtitleLanguages = null,
        IEnumerable<ResolutionSettings>? minSizeByResolution = null)
    {
        return new QualityProfile
        {
            MinimumSeeders = minimumSeeders,
            RequiredAudioLanguages = requiredAudioLanguages?.ToList() ?? ["German", "English"],
            PreferredAudioLanguages = ["German", "English"],
            RequiredSubtitleLanguages = requiredSubtitleLanguages?.ToList() ?? ["German", "English"],
            PreferredSubtitleLanguages = ["German", "English"],
            MinFileSizeByResolution = minSizeByResolution?.ToList() ?? [new ResolutionSettings { Resolution = "1080p", Bytes = 2L * 1024 * 1024 * 1024 }],
            MaxFileSizeByResolution = [new ResolutionSettings { Resolution = "1080p", Bytes = 20L * 1024 * 1024 * 1024 }, new ResolutionSettings { Resolution = "2160p", Bytes = 60L * 1024 * 1024 * 1024 }]
        };
    }
}
