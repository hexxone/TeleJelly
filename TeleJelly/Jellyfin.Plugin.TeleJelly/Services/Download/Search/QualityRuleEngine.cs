using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public sealed class QualityRuleEngine
{
    /// <summary>
    ///     Scores a search result based on the quality profile criteria.
    ///     Returns 0 if the result doesn't meet hard requirements (size limits, minimum seeders, required languages).
    ///     Higher scores indicate better quality matches.
    /// </summary>
    public static double ScoreResult(SearchResult result, QualityProfile profile)
    {
        double score = 0;

        // ===== HARD REQUIREMENTS (Disqualifying Checks) =====

        // 1. Minimum Seeders (ONLY for torrents - hosted downloads don't have seeders)
        if (result.ServiceType == DownloadServiceType.Torrent)
        {
            if (result.Seeders < profile.MinimumSeeders)
            {
                return 0; // Disqualify: Not enough seeders for reliable torrent download
            }
        }

        // 2. File Size Limits (Hard Requirements)
        // Skip checks if file size is unknown (0 or negative)
        if (result.FileSizeBytes > 0 && result.Resolution != null)
        {
            var maxSizeConfig = profile.MaxFileSizeByResolution.FirstOrDefault(r => r.Resolution == result.Resolution);
            if (maxSizeConfig != null && result.FileSizeBytes > maxSizeConfig.Bytes)
            {
                return 0; // Disqualify: File too large
            }

            var minSizeConfig = profile.MinFileSizeByResolution.FirstOrDefault(r => r.Resolution == result.Resolution);
            if (minSizeConfig != null && result.FileSizeBytes < minSizeConfig.Bytes)
            {
                return 0; // Disqualify: File too small (likely fake/sample)
            }
        }

        // 3. Required Audio Languages (MUST have at least one)
        // Only enforce if we have audio language metadata AND requirements are configured
        if (profile.RequiredAudioLanguages.Count > 0 && result.AudioLanguages.Length > 0)
        {
            var hasRequiredAudio = profile.RequiredAudioLanguages
                .Any(required => result.AudioLanguages.Any(available =>
                    available.Equals(required, StringComparison.OrdinalIgnoreCase)));

            if (!hasRequiredAudio)
            {
                return 0; // Disqualify: Missing required audio language
            }
        }

        // 4. Required Subtitle Languages (MUST have at least one)
        // Only enforce if we have subtitle language metadata AND requirements are configured
        if (profile.RequiredSubtitleLanguages.Count > 0 && result.SubtitleLanguages.Length > 0)
        {
            var hasRequiredSubtitles = profile.RequiredSubtitleLanguages
                .Any(required => result.SubtitleLanguages.Any(available =>
                    available.Equals(required, StringComparison.OrdinalIgnoreCase)));

            if (!hasRequiredSubtitles)
            {
                return 0; // Disqualify: Missing required subtitle language
            }
        }

        // ===== BASE SCORE =====
        // All results that pass hard requirements get a base score to prevent 0-scoring
        // This ensures results without metadata still have a chance to be selected
        score += 100;

        // ===== SCORING (Preference-Based) =====

        var weights = profile.Weights;

        // 5. Resolution Score (Highest Weight)
        var resolutions = profile.PreferredResolutions.ToArray();
        var resIndex = Array.IndexOf(resolutions, result.Resolution);
        if (resIndex != -1)
        {
            // Higher preference = more points (e.g., 2160p at position 0 = 3000, 1080p at position 1 = 2000)
            score += (resolutions.Length - resIndex) * weights.ResolutionPerPosition;
        }

        // 6. Video Codec Score (Preferred codecs get bonus)
        if (result.Codec != null)
        {
            var codecIndex = profile.PreferredCodecs.FindIndex(c =>
                c.Equals(result.Codec, StringComparison.OrdinalIgnoreCase));

            if (codecIndex != -1)
            {
                // Higher preference = more points
                score += (profile.PreferredCodecs.Count - codecIndex) * weights.CodecPerPosition;
            }
        }

        // 7. HDR Score (Preferred HDR formats get bonus)
        if (!string.IsNullOrEmpty(result.HDR))
        {
            var hdrIndex = profile.PreferredHDR.FindIndex(h =>
                h.Equals(result.HDR, StringComparison.OrdinalIgnoreCase));

            if (hdrIndex != -1)
            {
                // Higher preference = more points
                score += (profile.PreferredHDR.Count - hdrIndex) * weights.HdrPerPosition;
            }
        }

        // 8. Source Quality Score (BluRay > WEB-DL > WEBRip)
        if (result.Source != null)
        {
            var sourceIndex = profile.PreferredSources.FindIndex(s =>
                s.Equals(result.Source, StringComparison.OrdinalIgnoreCase));

            if (sourceIndex != -1)
            {
                // Higher preference = more points
                score += (profile.PreferredSources.Count - sourceIndex) * weights.SourcePerPosition;
            }
        }

        // 9. Audio Codec Score (Best matching codec wins)
        var bestAudioCodecIndex = result.AudioCodecs
            .Select(codec => profile.PreferredAudioCodecs.FindIndex(preferred =>
                preferred.Equals(codec, StringComparison.OrdinalIgnoreCase)))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        if (bestAudioCodecIndex >= 0)
        {
            score += (profile.PreferredAudioCodecs.Count - bestAudioCodecIndex) * weights.AudioCodecPerPosition;
        }

        // 10. Preferred Audio Languages Score (Bonus for each match)
        var preferredAudioMatches = profile.PreferredAudioLanguages
            .Count(preferred => result.AudioLanguages.Any(available =>
                available.Equals(preferred, StringComparison.OrdinalIgnoreCase)));
        score += preferredAudioMatches * weights.PreferredAudioLanguagePerMatch;

        // 11. Preferred Subtitle Languages Score (Bonus for each match)
        var preferredSubtitleMatches = profile.PreferredSubtitleLanguages
            .Count(preferred => result.SubtitleLanguages.Any(available =>
                available.Equals(preferred, StringComparison.OrdinalIgnoreCase)));
        score += preferredSubtitleMatches * weights.PreferredSubtitleLanguagePerMatch;

        // 12. Bitrate Score (Higher bitrate gets a bounded bonus when known)
        if (result.Bitrate.HasValue && result.Bitrate.Value > 0)
        {
            score += Math.Min(result.Bitrate.Value / 1000d * weights.BitratePerMbps, weights.MaxBitrateBonus);
        }

        // 13. Seeder Score (More seeders = more reliable, capped)
        // Note: Only applies to torrents - hosted downloads always have Seeders = 0
        if (result.ServiceType == DownloadServiceType.Torrent)
        {
            score += Math.Min(result.Seeders * weights.SeederMultiplier, weights.MaxSeederBonus);
        }

        return score;
    }

    /// <summary>
    ///     Calculates age-based scoring context by analyzing the date distribution of all results.
    ///     This implements a two-factor age scoring system:
    ///     1. Absolute Freshness: How old is the newest result from today?
    ///     2. Relative Spread: How clustered/spread are results relative to each other?
    /// </summary>
    private AgeContext CalculateAgeContext(List<SearchResult> results, ScoringWeights weights)
    {
        var context = new AgeContext();

        // Filter results that have upload dates
        var resultsWithDates = results.Where(r => r.UploadedDate.HasValue).ToList();

        if (resultsWithDates.Count == 0)
        {
            // No date information available
            context.HasDateInfo = false;
            return context;
        }

        context.HasDateInfo = true;

        // Step 1: Calculate Absolute Age (Newest Result)
        var newestDate = resultsWithDates.Max(r => r.UploadedDate!.Value);
        var oldestDate = resultsWithDates.Min(r => r.UploadedDate!.Value);

        context.NewestDate = newestDate;
        context.OldestDate = oldestDate;

        var newestResultAge = (DateTime.UtcNow - newestDate).TotalDays;

        // Step 2: Determine Absolute Freshness Factor
        if (newestResultAge <= weights.AbsoluteFreshThreshold1Days)
        {
            context.AbsoluteFreshness = weights.AbsoluteFreshnessFactor1; // 1.0 - Full impact
        }
        else if (newestResultAge <= weights.AbsoluteFreshThreshold2Days)
        {
            context.AbsoluteFreshness = weights.AbsoluteFreshnessFactor2; // 0.8 - 80% impact
        }
        else if (newestResultAge <= weights.AbsoluteFreshThreshold3Days)
        {
            context.AbsoluteFreshness = weights.AbsoluteFreshnessFactor3; // 0.5 - 50% impact
        }
        else if (newestResultAge <= weights.AbsoluteFreshThreshold4Days)
        {
            context.AbsoluteFreshness = weights.AbsoluteFreshnessFactor4; // 0.3 - 30% impact
        }
        else
        {
            context.AbsoluteFreshness = weights.AbsoluteFreshnessFactor5; // 0.1 - 10% impact (minimal)
        }

        // Step 3: Calculate Relative Spread Impact
        var dateSpreadDays = (newestDate - oldestDate).TotalDays;
        context.DateSpreadDays = dateSpreadDays;

        if (dateSpreadDays == 0)
        {
            context.BaseAgeImpact = 0.0; // All same date, no age factor
        }
        else if (dateSpreadDays <= weights.RecentReleaseThresholdDays)
        {
            context.BaseAgeImpact = weights.RecentReleaseAgeImpact; // 15% max bonus
        }
        else if (dateSpreadDays <= weights.ModerateAgeThresholdDays)
        {
            context.BaseAgeImpact = weights.ModerateAgeImpact; // 11% max bonus
        }
        else if (dateSpreadDays <= weights.OldContentThresholdDays)
        {
            context.BaseAgeImpact = weights.OldContentAgeImpact; // 8% max bonus
        }
        else
        {
            context.BaseAgeImpact = weights.ArchivedContentAgeImpact; // 5% max bonus
        }

        // Step 4: Combine Both Factors
        context.FinalAgeImpact = context.BaseAgeImpact * context.AbsoluteFreshness;

        return context;
    }

    /// <summary>
    ///     Context information for age-based scoring across all results.
    /// </summary>
    private sealed class AgeContext
    {
        public bool HasDateInfo { get; set; }
        public DateTime NewestDate { get; set; }
        public DateTime OldestDate { get; set; }
        public double DateSpreadDays { get; set; }
        public double AbsoluteFreshness { get; set; }
        public double BaseAgeImpact { get; set; }
        public double FinalAgeImpact { get; set; }
    }

    /// <summary>
    ///     Gets detailed scoring breakdown for a result. Useful for debugging and understanding why certain results scored the way they did.
    ///     This overload calculates age scoring based on all results in the set.
    /// </summary>
    public ScoringBreakdown GetScoringBreakdown(SearchResult result, QualityProfile profile, IEnumerable<SearchResult>? allResults = null)
    {
        var breakdown = new ScoringBreakdown
        {
            Title = result.Title,
            ServiceType = result.ServiceType.ToString()
        };

        // Check hard requirements
        if (result.ServiceType == DownloadServiceType.Torrent && result.Seeders < profile.MinimumSeeders)
        {
            breakdown.Disqualified = true;
            breakdown.DisqualificationReason = $"Insufficient seeders: {result.Seeders} < {profile.MinimumSeeders}";
            return breakdown;
        }

        if (result.FileSizeBytes > 0 && result.Resolution != null)
        {
            var maxSizeConfig = profile.MaxFileSizeByResolution.FirstOrDefault(r => r.Resolution == result.Resolution);
            if (maxSizeConfig != null && result.FileSizeBytes > maxSizeConfig.Bytes)
            {
                breakdown.Disqualified = true;
                breakdown.DisqualificationReason = $"File too large: {result.FileSizeBytes} > {maxSizeConfig.Bytes}";
                return breakdown;
            }

            var minSizeConfig = profile.MinFileSizeByResolution.FirstOrDefault(r => r.Resolution == result.Resolution);
            if (minSizeConfig != null && result.FileSizeBytes < minSizeConfig.Bytes)
            {
                breakdown.Disqualified = true;
                breakdown.DisqualificationReason = $"File too small: {result.FileSizeBytes} < {minSizeConfig.Bytes}";
                return breakdown;
            }
        }

        if (profile.RequiredAudioLanguages.Count > 0 && result.AudioLanguages.Length > 0)
        {
            var hasRequiredAudio = profile.RequiredAudioLanguages
                .Any(required => result.AudioLanguages.Any(available =>
                    available.Equals(required, StringComparison.OrdinalIgnoreCase)));

            if (!hasRequiredAudio)
            {
                breakdown.Disqualified = true;
                breakdown.DisqualificationReason = $"Missing required audio language. Required: [{string.Join(", ", profile.RequiredAudioLanguages)}], Available: [{string.Join(", ", result.AudioLanguages)}]";
                return breakdown;
            }
        }

        if (profile.RequiredSubtitleLanguages.Count > 0 && result.SubtitleLanguages.Length > 0)
        {
            var hasRequiredSubtitles = profile.RequiredSubtitleLanguages
                .Any(required => result.SubtitleLanguages.Any(available =>
                    available.Equals(required, StringComparison.OrdinalIgnoreCase)));

            if (!hasRequiredSubtitles)
            {
                breakdown.Disqualified = true;
                breakdown.DisqualificationReason = $"Missing required subtitle language. Required: [{string.Join(", ", profile.RequiredSubtitleLanguages)}], Available: [{string.Join(", ", result.SubtitleLanguages)}]";
                return breakdown;
            }
        }

        // Calculate scores
        var weights = profile.Weights;
        breakdown.BaseScore = 100;

        // Resolution
        var resolutions = profile.PreferredResolutions.ToArray();
        var resIndex = Array.IndexOf(resolutions, result.Resolution);
        if (resIndex != -1)
        {
            breakdown.ResolutionScore = (resolutions.Length - resIndex) * weights.ResolutionPerPosition;
        }

        // Codec
        if (result.Codec != null)
        {
            var codecIndex = profile.PreferredCodecs.FindIndex(c =>
                c.Equals(result.Codec, StringComparison.OrdinalIgnoreCase));
            if (codecIndex != -1)
            {
                breakdown.CodecScore = (profile.PreferredCodecs.Count - codecIndex) * weights.CodecPerPosition;
            }
        }

        // HDR
        if (!string.IsNullOrEmpty(result.HDR))
        {
            var hdrIndex = profile.PreferredHDR.FindIndex(h =>
                h.Equals(result.HDR, StringComparison.OrdinalIgnoreCase));
            if (hdrIndex != -1)
            {
                breakdown.HdrScore = (profile.PreferredHDR.Count - hdrIndex) * weights.HdrPerPosition;
            }
        }

        // Source
        if (result.Source != null)
        {
            var sourceIndex = profile.PreferredSources.FindIndex(s =>
                s.Equals(result.Source, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex != -1)
            {
                breakdown.SourceScore = (profile.PreferredSources.Count - sourceIndex) * weights.SourcePerPosition;
            }
        }

        // Audio codecs
        var bestAudioCodecIndex = result.AudioCodecs
            .Select(codec => profile.PreferredAudioCodecs.FindIndex(preferred =>
                preferred.Equals(codec, StringComparison.OrdinalIgnoreCase)))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (bestAudioCodecIndex >= 0)
        {
            breakdown.AudioCodecScore = (profile.PreferredAudioCodecs.Count - bestAudioCodecIndex) * weights.AudioCodecPerPosition;
        }

        // Languages
        var preferredAudioMatches = profile.PreferredAudioLanguages
            .Count(preferred => result.AudioLanguages.Any(available =>
                available.Equals(preferred, StringComparison.OrdinalIgnoreCase)));
        breakdown.AudioLanguageScore = preferredAudioMatches * weights.PreferredAudioLanguagePerMatch;

        var preferredSubtitleMatches = profile.PreferredSubtitleLanguages
            .Count(preferred => result.SubtitleLanguages.Any(available =>
                available.Equals(preferred, StringComparison.OrdinalIgnoreCase)));
        breakdown.SubtitleLanguageScore = preferredSubtitleMatches * weights.PreferredSubtitleLanguagePerMatch;

        // Bitrate
        if (result.Bitrate.HasValue && result.Bitrate.Value > 0)
        {
            breakdown.BitrateScore = Math.Min(result.Bitrate.Value / 1000d * weights.BitratePerMbps, weights.MaxBitrateBonus);
        }

        // Seeders
        if (result.ServiceType == DownloadServiceType.Torrent)
        {
            breakdown.SeederScore = Math.Min(result.Seeders * weights.SeederMultiplier, weights.MaxSeederBonus);
        }

        breakdown.BaseQualityScore = breakdown.BaseScore + breakdown.ResolutionScore + breakdown.CodecScore +
                                     breakdown.HdrScore + breakdown.SourceScore + breakdown.AudioCodecScore +
                                     breakdown.AudioLanguageScore + breakdown.SubtitleLanguageScore +
                                     breakdown.BitrateScore + breakdown.SeederScore;

        // Age scoring (if all results provided)
        if (allResults != null)
        {
            var resultsList = allResults.ToList();
            var ageContext = CalculateAgeContext(resultsList, weights);

            if (ageContext.HasDateInfo && result.UploadedDate.HasValue)
            {
                // Calculate relative position
                double relativeAge = 0.0;
                if (ageContext.DateSpreadDays > 0)
                {
                    var ageInDays = (ageContext.NewestDate - result.UploadedDate.Value).TotalDays;
                    relativeAge = ageInDays / ageContext.DateSpreadDays;
                }

                var recencyFactor = 1.0 - relativeAge;
                var ageMultiplier = 1.0 + (ageContext.FinalAgeImpact * recencyFactor);

                breakdown.AgeMultiplier = ageMultiplier;
                breakdown.AgeBonus = breakdown.BaseQualityScore * (ageMultiplier - 1.0);
                breakdown.DateSpreadDays = ageContext.DateSpreadDays;
                breakdown.AbsoluteFreshness = ageContext.AbsoluteFreshness;
                breakdown.BaseAgeImpact = ageContext.BaseAgeImpact;
                breakdown.FinalAgeImpact = ageContext.FinalAgeImpact;
                breakdown.UploadedDate = result.UploadedDate.Value;
                breakdown.DaysOld = (DateTime.UtcNow - result.UploadedDate.Value).TotalDays;
            }
        }

        breakdown.TotalScore = breakdown.BaseQualityScore * breakdown.AgeMultiplier;

        return breakdown;
    }
}
