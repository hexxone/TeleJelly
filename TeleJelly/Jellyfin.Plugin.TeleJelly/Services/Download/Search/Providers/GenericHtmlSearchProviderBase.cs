using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal abstract class GenericHtmlSearchProviderBase : ISearchProvider
{
    private readonly ISearchDocumentFetcher _fetcher;
    private readonly ILogger _logger;

    protected GenericHtmlSearchProviderBase(string name, string searchUrlTemplate, ILogger logger, ISearchDocumentFetcher? fetcher = null)
    {
        Name = name;
        SearchUrlTemplate = searchUrlTemplate;
        _logger = logger;
        _fetcher = fetcher ?? new HttpClientSearchDocumentFetcher();
    }

    public string Name { get; }
    protected string SearchUrlTemplate { get; }

    public virtual async Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct)
    {
        var terms = new[] { imdbId, query }
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var links = new List<string>();
        foreach (var term in terms)
        {
            var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, SearchUrlTemplate, Uri.EscapeDataString(term));
            try
            {
                var html = await _fetcher.GetStringAsync(new Uri(url, UriKind.Absolute), ct);
                links.AddRange(ExtractCandidateLinks(html));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} request failed for {Url}", Name, url);
            }
        }

        var dedupedLinks = links
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToArray();

        var results = dedupedLinks.Select(link => new SearchResult
        {
            Title = query,
            DownloadLink = link,
            ServiceType = IsMagnet(link) ? DownloadServiceType.Torrent : DownloadServiceType.Hosted,
            Source = Name
        }).ToArray();

        return results;
    }

    internal static IEnumerable<string> ExtractCandidateLinks(string html)
    {
        // Keep extractor intentionally broad because most providers differ and some are not stable.
        var linkRegex = new Regex(@"(magnet:\?xt=urn:btih:[^""'\s<]+|https?://[^""'\s<]+)", RegexOptions.IgnoreCase);
        foreach (Match match in linkRegex.Matches(html))
        {
            var value = match.Value.Trim();
            if (value.Contains("/wp-content/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return value;
        }
    }

    private static bool IsMagnet(string link)
    {
        return link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
    }
}
