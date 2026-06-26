using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public class SearchOrchestrator
{
    private readonly IEnumerable<ISearchProvider> _providers;
    private readonly QualityRuleEngine _qualityEngine;
    private readonly ILogger<SearchOrchestrator> _logger;

    public SearchOrchestrator(IEnumerable<ISearchProvider> providers, QualityRuleEngine qualityEngine, ILogger<SearchOrchestrator> logger)
    {
        _providers = providers;
        _qualityEngine = qualityEngine;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAndRankAsync(
        string query,
        string? imdbId,
        QualityProfile profile,
        int maxResults,
        CancellationToken ct,
        IEnumerable<string>? enabledProviders = null,
        long maxDownloadSizeBytes = 0)
    {
        var allResults = new List<SearchResult>();

        var enabledProviderSet = enabledProviders != null
            ? new HashSet<string>(enabledProviders, System.StringComparer.OrdinalIgnoreCase)
            : null;

        var providers = _providers
            .Where(p => enabledProviderSet == null || enabledProviderSet.Count == 0 || enabledProviderSet.Contains(p.Name))
            .ToArray();

        foreach (var provider in providers)
        {
            try
            {
                var results = await provider.SearchAsync(query, imdbId, ct);
                allResults.AddRange(results);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Search provider {Provider} failed for query {Query}", provider.Name, query);
            }
        }

        if (allResults.Count == 0)
        {
            return [];
        }

        var eligibleResults = allResults
            .Where(result => result.FileSizeBytes <= 0 || maxDownloadSizeBytes <= 0 || result.FileSizeBytes <= maxDownloadSizeBytes)
            .ToList();

        if (eligibleResults.Count == 0)
        {
            _logger.LogInformation(
                "Discarded all {ResultCount} search results for query {Query} because they exceeded the configured size limit of {SizeLimit} bytes",
                allResults.Count,
                query,
                maxDownloadSizeBytes);
            return [];
        }

        var ranked = eligibleResults
            .Select(result =>
            {
                var breakdown = _qualityEngine.GetScoringBreakdown(result, profile, eligibleResults);
                result.QualityScore = breakdown.TotalScore;
                return new { Result = result, Breakdown = breakdown };
            })
            .Where(entry => !entry.Breakdown.Disqualified && entry.Breakdown.TotalScore > 0)
            .OrderByDescending(entry => entry.Breakdown.TotalScore)
            .ThenByDescending(entry => entry.Result.Seeders)
            .Take(maxResults)
            .Select(entry => entry.Result)
            .ToArray();

        return ranked;
    }
}
