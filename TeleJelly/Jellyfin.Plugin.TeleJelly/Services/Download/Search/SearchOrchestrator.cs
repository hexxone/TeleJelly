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
        IEnumerable<string>? enabledProviders = null)
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

        var ranked = allResults
            .Select(result =>
            {
                var score = _qualityEngine.ScoreResult(result, profile);
                result.QualityScore = score;
                return result;
            })
            .Where(r => r.QualityScore > 0)
            .OrderByDescending(r => r.QualityScore)
            .ThenByDescending(r => r.Seeders)
            .Take(maxResults)
            .ToArray();

        return ranked;
    }

    public async Task<SearchResult?> FindBestMatch(string query, string? imdbId, QualityProfile profile, CancellationToken ct, IEnumerable<string>? enabledProviders = null)
    {
        var ranked = await SearchAndRankAsync(query, imdbId, profile, 1, ct, enabledProviders);
        return ranked.FirstOrDefault();
    }
}
