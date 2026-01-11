using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public class QualityRuleEngine
{
    /// <summary>
    ///     TODO make all given and multiplied scores configurable as well..
    /// </summary>
    public double ScoreResult(SearchResult result, QualityProfile profile)
    {
        double score = 0;

        // 1. Resolution (Highest Weight)
        var resIndex = Array.IndexOf(profile.PreferredResolutions, result.Resolution);
        if (resIndex != -1)
        {
            // Give more points to resolutions higher in the preference list
            score += (profile.PreferredResolutions.Length - resIndex) * 1000;
        }

        // 2. Codec & HDR
        if (profile.PreferredCodecs.Contains(result.Codec))
        {
            score += 500;
        }

        if (!string.IsNullOrEmpty(result.HDR) && profile.PreferredHDR.Contains(result.HDR))
        {
            score += 300;
        }

        // 3. Size Limits (Hard Requirement)
        if (result.Resolution != null && profile.MaxFileSizeByResolution.TryGetValue(result.Resolution, out var maxSize))
        {
            if (result.FileSizeBytes > maxSize)
            {
                return 0; // Disqualify
            }
        }

        // 4. Seeders (Reliability)
        score += Math.Min(result.Seeders * 10, 500);

        // 5. Source (BluRay > WEB-DL)
        if (profile.PreferredSources.Contains(result.Source))
        {
            score += 200;
        }

        return score;
    }

    /// <summary>
    ///     Selects the best search result from the provided collection of search results based on the specified quality profile.
    /// </summary>
    /// <param name="results">A collection of search results to evaluate.</param>
    /// <param name="profile">The quality profile containing criteria used to score and determine the best result.</param>
    /// <returns>The search result with the highest score that meets the quality criteria, or null if no suitable result is found.</returns>
    public SearchResult? SelectBestResult(IEnumerable<SearchResult> results, QualityProfile profile)
    {
        return results
            .Select(r => new { Result = r, Score = ScoreResult(r, profile) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Result;
    }
}
