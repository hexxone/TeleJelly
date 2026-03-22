using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

// TODO unused ??
public class SearchOrchestrator
{
    private readonly IEnumerable<ISearchProvider> _providers;
    private readonly QualityRuleEngine _qualityEngine;

    public SearchOrchestrator(IEnumerable<ISearchProvider> providers, QualityRuleEngine qualityEngine)
    {
        _providers = providers;
        _qualityEngine = qualityEngine;
    }

    // TODO unused ??
    public async Task<SearchResult?> FindBestMatch(string query, QualityProfile profile, CancellationToken ct)
    {
        var allResults = new List<SearchResult>();
        foreach (var provider in _providers)
        {
            var results = await provider.SearchAsync(query, ct);
            allResults.AddRange(results);
        }

        return _qualityEngine.SelectBestResult(allResults, profile);
    }
}
