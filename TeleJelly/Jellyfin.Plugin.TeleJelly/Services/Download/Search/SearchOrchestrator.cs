using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public class SearchOrchestrator
{
    private static readonly Regex YearRegex = new(@"\b(?:19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex SeasonRegex = new(@"\bS(?<season>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int MaxTitleSearchesPerProvider = 8;

    private readonly IEnumerable<ISearchProvider> _providers;
    private readonly QualityRuleEngine _qualityEngine;
    private readonly ILogger<SearchOrchestrator> _logger;
    private readonly IDownloadLinkValidator? _linkValidator;

    public SearchOrchestrator(
        IEnumerable<ISearchProvider> providers,
        QualityRuleEngine qualityEngine,
        ILogger<SearchOrchestrator> logger,
        IDownloadLinkValidator? linkValidator = null)
    {
        _providers = providers;
        _qualityEngine = qualityEngine;
        _logger = logger;
        _linkValidator = linkValidator;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAndRankAsync(
        string query,
        string? imdbId,
        QualityProfile profile,
        int maxResults,
        CancellationToken ct,
        IEnumerable<string>? enabledProviders = null,
        long maxDownloadSizeBytes = 0,
        IEnumerable<string>? titleAliases = null,
        SearchProgress? progress = null)
    {
        var allResults = new List<SearchResult>();
        var expectedTitles = (titleAliases ?? [])
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var searchQueries = BuildSearchQueries(query, expectedTitles).ToArray();

        var enabledProviderSet = enabledProviders != null
            ? new HashSet<string>(enabledProviders, System.StringComparer.OrdinalIgnoreCase)
            : null;

        var providers = _providers
            .Where(p => enabledProviderSet == null || enabledProviderSet.Count == 0 || enabledProviderSet.Contains(p.Name))
            .ToArray();

        progress?.Configure(providers.Length, searchQueries.Length);

        _logger.LogInformation(
            "Searching {ProviderCount} providers for {Query}: {Providers}",
            providers.Length,
            query,
            string.Join(", ", providers.Select(provider => provider.Name)));

        var providerStopwatch = Stopwatch.StartNew();
        var providerTasks = providers
            .Select(provider => SearchProviderAsync(provider, searchQueries, imdbId, progress, ct))
            .ToArray();
        var providerResults = await Task.WhenAll(providerTasks);

        // Task.WhenAll preserves the input task order, so providers can execute in
        // parallel without making equal-score result ordering nondeterministic.
        foreach (var results in providerResults)
        {
            allResults.AddRange(results);
        }

        if (_linkValidator != null && allResults.Count > 0)
        {
            allResults = await RemoveBrokenDownloadLinksAsync(allResults, progress, ct);
        }

        _logger.LogInformation(
            "Parallel provider search completed in {ElapsedMilliseconds} ms with {ResultCount} total result(s) for {Query}",
            providerStopwatch.ElapsedMilliseconds,
            allResults.Count,
            query);

        if (allResults.Count == 0)
        {
            _logger.LogInformation("Search summary for {Query}: no provider returned an actionable download", query);
            return [];
        }

        var sizeRejectedResults = allResults
            .Where(result => result.FileSizeBytes > 0 && maxDownloadSizeBytes > 0 && result.FileSizeBytes > maxDownloadSizeBytes)
            .ToArray();
        var sizeEligibleResults = allResults
            .Except(sizeRejectedResults)
            .ToList();

        if (sizeEligibleResults.Count == 0)
        {
            _logger.LogInformation(
                "Search summary for {Query}: {ResultCount} download(s) found, all rejected because they exceeded the global size limit of {SizeLimit} bytes",
                query,
                allResults.Count,
                maxDownloadSizeBytes);
            return [];
        }

        var mismatchedResults = sizeEligibleResults
            .Select(result => new
            {
                Result = result,
                Reason = GetMetadataMismatchReason(query, result) ?? GetTitleMismatchReason(result, expectedTitles, imdbId)
            })
            .Where(entry => entry.Reason != null)
            .ToArray();
        var matchingResults = sizeEligibleResults
            .Except(mismatchedResults.Select(entry => entry.Result))
            .ToList();

        foreach (var mismatch in mismatchedResults)
        {
            _logger.LogDebug(
                "Rejected search result {Title} from {Provider} for {Query}: {Reason}",
                mismatch.Result.Title,
                mismatch.Result.Provider,
                query,
                mismatch.Reason);
        }

        if (matchingResults.Count == 0)
        {
            _logger.LogInformation(
                "Search summary for {Query}: {FoundCount} download(s) found, {SizeRejectedCount} rejected by the global size limit, and {MismatchCount} rejected for conflicting or unrelated metadata",
                query,
                allResults.Count,
                sizeRejectedResults.Length,
                mismatchedResults.Length);
            return [];
        }

        var scored = matchingResults
            .Select(result =>
            {
                var breakdown = _qualityEngine.GetScoringBreakdown(result, profile, matchingResults);
                return new { Result = result, Breakdown = breakdown };
            })
            .ToArray();

        var strictResults = scored
            .Where(entry => !entry.Breakdown.Disqualified && entry.Breakdown.TotalScore > 0)
            .ToArray();
        var useQualityFallback = strictResults.Length == 0;
        var candidates = useQualityFallback ? scored : strictResults;

        foreach (var rejected in scored.Where(entry => entry.Breakdown.Disqualified))
        {
            _logger.LogDebug(
                "Search result {Title} from {Provider} failed strict quality rules: {Reasons}",
                rejected.Result.Title,
                rejected.Result.Provider,
                rejected.Breakdown.DisqualificationReason);
        }

        var qualityReasonSummary = scored
            .Where(entry => entry.Breakdown.Disqualified)
            .SelectMany(entry => entry.Breakdown.DisqualificationReasons)
            .GroupBy(GetQualityReasonCategory)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => $"{group.Count()} {group.Key}")
            .ToArray();

        if (qualityReasonSummary.Length > 0)
        {
            _logger.LogInformation(
                "Strict quality rejection reasons for {Query}: {Reasons}",
                query,
                string.Join(", ", qualityReasonSummary));
        }

        var ranked = candidates
            .OrderBy(entry => entry.Breakdown.DisqualificationReasons.Count)
            .ThenByDescending(entry => entry.Breakdown.TotalScore)
            .ThenByDescending(entry => entry.Result.Seeders)
            .Take(maxResults)
            .Select(entry =>
            {
                entry.Result.QualityFallback = useQualityFallback;
                entry.Result.QualityScore = useQualityFallback
                    ? entry.Breakdown.TotalScore / (4d * System.Math.Max(1, entry.Breakdown.DisqualificationReasons.Count))
                    : entry.Breakdown.TotalScore;
                return entry.Result;
            })
            .ToArray();

        if (useQualityFallback)
        {
            foreach (var fallback in ranked)
            {
                var breakdown = scored.First(entry => ReferenceEquals(entry.Result, fallback)).Breakdown;
                _logger.LogInformation(
                    "Returning quality fallback {Title} from {Provider}: {Reasons}",
                    fallback.Title,
                    fallback.Provider,
                    breakdown.DisqualificationReason);
            }
        }

        _logger.LogInformation(
            "Search summary for {Query}: {FoundCount} download(s) found, {SizeRejectedCount} rejected by the global size limit, {MismatchCount} rejected for conflicting metadata, {QualityRejectedCount} failed strict quality rules, {StrictCount} met strict quality rules, returning {ReturnedCount}{FallbackSuffix}",
            query,
            allResults.Count,
            sizeRejectedResults.Length,
            mismatchedResults.Length,
            scored.Count(entry => entry.Breakdown.Disqualified),
            strictResults.Length,
            ranked.Length,
            useQualityFallback ? " as quality fallback(s)" : string.Empty);

        return ranked;
    }

    private async Task<SearchResult[]> SearchProviderAsync(
        ISearchProvider provider,
        IReadOnlyList<string> queries,
        string? imdbId,
        SearchProgress? progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var results = new List<SearchResult>();
            var seenLinks = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < queries.Count; index++)
            {
                try
                {
                    var queryResults = await provider.SearchAsync(queries[index], index == 0 ? imdbId : null, ct);
                    foreach (var result in queryResults)
                    {
                        if (seenLinks.Add(result.DownloadLink))
                        {
                            results.Add(result);
                        }
                    }
                }
                finally
                {
                    progress?.CompleteWorkUnit();
                }
            }

            foreach (var result in results)
            {
                result.Provider = provider.Name;
            }

            _logger.LogInformation(
                "Search provider {Provider} returned {ResultCount} result(s) for {Query} in {ElapsedMilliseconds} ms",
                provider.Name,
                results.Count,
                queries[0],
                stopwatch.ElapsedMilliseconds);
            return results.ToArray();
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Search provider {Provider} failed for query {Query} after {ElapsedMilliseconds} ms",
                provider.Name,
                queries[0],
                stopwatch.ElapsedMilliseconds);
            return [];
        }
        finally
        {
            progress?.CompleteProvider();
        }
    }

    private async Task<List<SearchResult>> RemoveBrokenDownloadLinksAsync(
        IReadOnlyCollection<SearchResult> results,
        SearchProgress? progress,
        CancellationToken ct)
    {
        var links = results
            .Select(result => result.DownloadLink)
            .Where(_linkValidator!.CanValidate)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (links.Length == 0)
        {
            return results.ToList();
        }

        progress?.AddLinkValidationWork(links.Length);
        var validationTasks = links.Select(async link =>
        {
            try
            {
                return new
                {
                    Link = link,
                    Status = await _linkValidator!.ValidateAsync(link, ct)
                };
            }
            finally
            {
                progress?.CompleteWorkUnit();
            }
        });
        var validations = await Task.WhenAll(validationTasks);
        var brokenLinks = validations
            .Where(validation => validation.Status == DownloadLinkValidationStatus.Broken)
            .Select(validation => validation.Link)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        if (brokenLinks.Count > 0)
        {
            _logger.LogInformation("Filtered {BrokenLinkCount} broken FileCrypt container(s) before ranking", brokenLinks.Count);
        }

        return results.Where(result => !brokenLinks.Contains(result.DownloadLink)).ToList();
    }

    private static IEnumerable<string> BuildSearchQueries(string primaryQuery, IReadOnlyList<string> titleAliases)
    {
        yield return primaryQuery;

        if (titleAliases.Count == 0)
        {
            yield break;
        }

        var suffixParts = YearRegex.Matches(primaryQuery)
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Value)
            .Concat(SeasonRegex.Matches(primaryQuery)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Value.ToUpperInvariant()))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var seenQueries = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { primaryQuery.Trim() };

        foreach (var alias in titleAliases)
        {
            var aliasQuery = string.Join(' ', new[] { alias }.Concat(suffixParts));
            if (seenQueries.Add(aliasQuery))
            {
                yield return aliasQuery;
                if (seenQueries.Count >= MaxTitleSearchesPerProvider)
                {
                    yield break;
                }
            }
        }
    }

    private static string? GetMetadataMismatchReason(string query, SearchResult result)
    {
        var expectedYears = YearRegex.Matches(query).Cast<System.Text.RegularExpressions.Match>().Select(match => match.Value).Distinct().ToArray();
        var resultYears = YearRegex.Matches(result.Title).Cast<System.Text.RegularExpressions.Match>().Select(match => match.Value).Distinct().ToArray();
        if (expectedYears.Length > 0 && resultYears.Length > 0 && !expectedYears.Intersect(resultYears).Any())
        {
            return $"year mismatch (expected {string.Join("/", expectedYears)}, found {string.Join("/", resultYears)})";
        }

        var expectedSeasonMatch = SeasonRegex.Match(query);
        var resultSeasonMatch = SeasonRegex.Match(result.Title);
        if (expectedSeasonMatch.Success && resultSeasonMatch.Success &&
            int.Parse(expectedSeasonMatch.Groups["season"].Value, System.Globalization.CultureInfo.InvariantCulture) !=
            int.Parse(resultSeasonMatch.Groups["season"].Value, System.Globalization.CultureInfo.InvariantCulture))
        {
            return $"season mismatch (expected S{expectedSeasonMatch.Groups["season"].Value}, found S{resultSeasonMatch.Groups["season"].Value})";
        }

        return null;
    }

    private static string? GetTitleMismatchReason(SearchResult result, IReadOnlyList<string> expectedTitles, string? imdbId)
    {
        if (expectedTitles.Count == 0 || SearchTitleMatcher.IsMatch(result, expectedTitles, imdbId))
        {
            return null;
        }

        return $"title does not match any known title ({string.Join(" / ", expectedTitles.Take(3))})";
    }

    private static string GetQualityReasonCategory(string reason)
    {
        if (reason.StartsWith("Insufficient seeders", System.StringComparison.Ordinal))
        {
            return "with too few seeders";
        }

        if (reason.StartsWith("File too large", System.StringComparison.Ordinal))
        {
            return "above the profile size range";
        }

        if (reason.StartsWith("File too small", System.StringComparison.Ordinal))
        {
            return "below the profile size range";
        }

        if (reason.StartsWith("Missing required audio", System.StringComparison.Ordinal))
        {
            return "without a required audio language";
        }

        if (reason.StartsWith("Missing required subtitle", System.StringComparison.Ordinal))
        {
            return "without a required subtitle language";
        }

        return "rejected by another quality rule";
    }
}
