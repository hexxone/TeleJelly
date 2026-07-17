using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal class ConfigurableStructuredSearchProvider : ISearchProvider
{
    private static readonly Regex AnchorRegex = new(@"<a[^>]+href=""(?<href>[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"(?<url>https?://[^\s""'<>]+|/(?:out|azn)/af\.php\?[^\s""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new(@"<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PasswordRegex = new(@"(?:Passwort|Password)\s*:?\s*(?<password>[^\r\n<]{1,120}?)(?=\s{2,}|Audio\b|Ton\b|Sprache\b|Untertitel\b|Subtitle\b|Size\b|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PostedRegex = new(@"Posted on (?<date>[A-Za-z]+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"\b(?<value>2160p|1080p|720p|576p|480p)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>TB|TiB|GB|GiB|MB|MiB|KB|KiB)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BitrateRegex = new(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>Mbps|Mbit/s|Mbit|kbps|kbit/s|kbit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultipartRegex = new(@"\b(part|cd|disc|disk|vol)\s*0*(?<part>\d{1,2})\b|\b(?<partA>\d{1,2})\s*/\s*(?<partB>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string[] _allowedSubtypes;
    private readonly Uri _baseUri;
    private readonly SearchDiscoveryMode _discoveryMode;
    private readonly ISearchDocumentFetcher _fetcher;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly PostFetchMode _postFetchMode;
    private readonly string _searchUrlTemplate;

    public ConfigurableStructuredSearchProvider(
        string name,
        string baseUrl,
        SearchDiscoveryMode discoveryMode,
        PostFetchMode postFetchMode,
        ILogger logger,
        string searchUrlTemplate = "?s={0}",
        params string[] allowedSubtypes)
        : this(name, baseUrl, discoveryMode, postFetchMode, logger, null, searchUrlTemplate, allowedSubtypes)
    {
    }

    internal ConfigurableStructuredSearchProvider(
        string name,
        string baseUrl,
        SearchDiscoveryMode discoveryMode,
        PostFetchMode postFetchMode,
        ILogger logger,
        ISearchDocumentFetcher? fetcher,
        string searchUrlTemplate = "?s={0}",
        params string[] allowedSubtypes)
    {
        _name = name;
        _baseUri = new Uri(baseUrl, UriKind.Absolute);
        _discoveryMode = discoveryMode;
        _postFetchMode = postFetchMode;
        _logger = logger;
        _fetcher = fetcher ?? new HttpClientSearchDocumentFetcher();
        _searchUrlTemplate = searchUrlTemplate;
        _allowedSubtypes = allowedSubtypes;
    }

    public string Name => _name;

    public async Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct)
    {
        var terms = BuildSearchTerms(query, imdbId).ToArray();
        var seenPageUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDownloadLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<SearchResult>();

        foreach (var term in terms)
        {
            IReadOnlyList<PageCandidate> candidates;
            try
            {
                candidates = await DiscoverCandidatesAsync(term, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} search discovery failed for term {Term}", Name, term);
                continue;
            }

            foreach (var candidate in candidates)
            {
                var normalizedPageUrl = NormalizeUrl(candidate.PageUrl);
                if (!seenPageUrls.Add(normalizedPageUrl))
                {
                    continue;
                }

                PostDocument? document;
                try
                {
                    document = await LoadDocumentAsync(candidate, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider {Provider} failed to crawl result page {Url}", Name, candidate.PageUrl);
                    continue;
                }

                if (document == null)
                {
                    continue;
                }

                foreach (var result in await BuildSearchResultsAsync(document, ct))
                {
                    if (seenDownloadLinks.Add(result.DownloadLink))
                    {
                        results.Add(result);
                    }
                }
            }

            if (results.Count >= 25)
            {
                break;
            }
        }

        return results.Take(25).ToArray();
    }

    private IEnumerable<string> BuildSearchTerms(string query, string? imdbId)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            yield return imdbId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            yield return query.Trim();
        }
    }

    private async Task<IReadOnlyList<PageCandidate>> DiscoverCandidatesAsync(string term, CancellationToken ct)
    {
        return _discoveryMode switch
        {
            SearchDiscoveryMode.WordPressRest => await DiscoverWordPressRestCandidatesAsync(term, ct),
            SearchDiscoveryMode.WordPressHtml => await DiscoverWordPressHtmlCandidatesAsync(term, ct),
            _ => []
        };
    }

    private async Task<IReadOnlyList<PageCandidate>> DiscoverWordPressRestCandidatesAsync(string term, CancellationToken ct)
    {
        var restUrl = new Uri(_baseUri, $"wp-json/wp/v2/search?search={Uri.EscapeDataString(term)}&per_page=10");
        var responseText = await _fetcher.GetStringAsync(restUrl, ct);
        if (!LooksLikeJson(responseText))
        {
            return await DiscoverWordPressHtmlCandidatesAsync(term, ct);
        }

        using var document = JsonDocument.Parse(responseText);
        var candidates = new List<PageCandidate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                !string.Equals(typeElement.GetString(), "post", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var subtype = item.TryGetProperty("subtype", out var subtypeElement)
                ? subtypeElement.GetString()
                : null;

            if (_allowedSubtypes.Length > 0 &&
                (subtype == null || !_allowedSubtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var pageUrl = item.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                continue;
            }

            string? detailUrl = null;
            if (item.TryGetProperty("_links", out var linksElement) &&
                linksElement.TryGetProperty("self", out var selfElement) &&
                selfElement.ValueKind == JsonValueKind.Array &&
                selfElement.GetArrayLength() > 0 &&
                selfElement[0].TryGetProperty("href", out var hrefElement))
            {
                detailUrl = hrefElement.GetString();
            }

            candidates.Add(new PageCandidate(
                pageUrl!,
                DecodeHtml(item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null),
                detailUrl));
        }

        return candidates;
    }

    private async Task<IReadOnlyList<PageCandidate>> DiscoverWordPressHtmlCandidatesAsync(string term, CancellationToken ct)
    {
        var searchUrl = new Uri(_baseUri, string.Format(CultureInfo.InvariantCulture, _searchUrlTemplate, Uri.EscapeDataString(term)));
        var html = await _fetcher.GetStringAsync(searchUrl, ct);
        var termTokens = ExtractQueryTokens(term).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<PageCandidate>();

        foreach (Match match in AnchorRegex.Matches(html))
        {
            var href = match.Groups["href"].Value.Trim();
            if (!TryMakeAbsolute(href, out var absoluteHref))
            {
                continue;
            }

            if (!LooksLikeResultPage(absoluteHref))
            {
                continue;
            }

            var normalized = NormalizeUrl(absoluteHref);
            if (!seen.Add(normalized))
            {
                continue;
            }

            var text = DecodeHtml(StripTags(match.Groups["text"].Value));
            var haystack = $"{absoluteHref} {text}";
            if (!MatchesSearchTerm(haystack, termTokens))
            {
                continue;
            }

            candidates.Add(new PageCandidate(absoluteHref, text, null));
        }

        return candidates.Take(10).ToArray();
    }

    private async Task<PostDocument?> LoadDocumentAsync(PageCandidate candidate, CancellationToken ct)
    {
        return _postFetchMode switch
        {
            PostFetchMode.WordPressJson => await LoadWordPressJsonDocumentAsync(candidate, ct),
            PostFetchMode.Html => await LoadHtmlDocumentAsync(candidate.PageUrl, candidate.Title, ct),
            PostFetchMode.HdEncodeProtectedHtml => await LoadHdEncodeDocumentAsync(candidate.PageUrl, candidate.Title, ct),
            _ => null
        };
    }

    private async Task<PostDocument?> LoadWordPressJsonDocumentAsync(PageCandidate candidate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidate.DetailUrl))
        {
            return await LoadHtmlDocumentAsync(candidate.PageUrl, candidate.Title, ct);
        }

        var responseText = await _fetcher.GetStringAsync(new Uri(candidate.DetailUrl, UriKind.Absolute), ct);
        if (!LooksLikeJson(responseText))
        {
            return await LoadHtmlDocumentAsync(candidate.PageUrl, candidate.Title, ct);
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var title = candidate.Title;
        if (root.TryGetProperty("title", out var titleElement) &&
            titleElement.TryGetProperty("rendered", out var renderedTitle))
        {
            title = DecodeHtml(renderedTitle.GetString());
        }

        var contentHtml = string.Empty;
        if (root.TryGetProperty("content", out var contentElement) &&
            contentElement.TryGetProperty("rendered", out var renderedContent))
        {
            contentHtml = renderedContent.GetString() ?? string.Empty;
        }

        DateTime? uploadedDate = null;
        if (root.TryGetProperty("date_gmt", out var dateElement) &&
            DateTime.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
        {
            uploadedDate = parsedDate;
        }

        return new PostDocument(candidate.PageUrl, title ?? string.Empty, contentHtml, uploadedDate);
    }

    private async Task<PostDocument?> LoadHtmlDocumentAsync(string pageUrl, string? fallbackTitle, CancellationToken ct)
    {
        var html = await _fetcher.GetStringAsync(new Uri(pageUrl, UriKind.Absolute), ct);
        var title = ExtractHtmlTitle(html);
        var uploadedDate = ExtractPostedDate(html);
        return new PostDocument(pageUrl, string.IsNullOrWhiteSpace(title) ? fallbackTitle ?? string.Empty : title, html, uploadedDate);
    }

    private async Task<PostDocument?> LoadHdEncodeDocumentAsync(string pageUrl, string? fallbackTitle, CancellationToken ct)
    {
        var lockedHtml = await _fetcher.GetStringAsync(new Uri(pageUrl, UriKind.Absolute), ct);
        var unlockPayload = ExtractHdEncodeUnlockPayload(lockedHtml);
        var finalHtml = lockedHtml;

        if (unlockPayload.Count > 0)
        {
            finalHtml = await _fetcher.PostFormAsync(new Uri(pageUrl, UriKind.Absolute), unlockPayload, ct);
        }

        var title = ExtractHtmlTitle(finalHtml);
        var uploadedDate = ExtractPostedDate(finalHtml) ?? ExtractPostedDate(lockedHtml);
        return new PostDocument(pageUrl, string.IsNullOrWhiteSpace(title) ? fallbackTitle ?? string.Empty : title, finalHtml, uploadedDate);
    }

    private async Task<IEnumerable<SearchResult>> BuildSearchResultsAsync(PostDocument document, CancellationToken ct)
    {
        var decodedText = DecodeHtml(StripTags(document.ContentHtml));
        var payloads = (await ExtractDownloadPayloadsAsync(document.ContentHtml, ct)).ToArray();
        if (payloads.Length == 0)
        {
            return [];
        }

        var password = ExtractPassword(document.ContentHtml, decodedText);
        var resolution = MatchValue(ResolutionRegex, $"{document.Title} {decodedText}");
        var codec = ParseCodec($"{document.Title} {decodedText}");
        var hdr = ParseHdr($"{document.Title} {decodedText}");
        var source = ParseSource($"{document.Title} {decodedText}");
        var audioCodecs = ParseAudioCodecs($"{document.Title} {decodedText}");
        var bitrate = ParseBitrateKbps(decodedText);
        var release = NormalizeReleaseName(document.Title);
        var fileSizeBytes = ParseEstimatedSizeBytes(decodedText, payloads);

        return payloads.Select(payload => new SearchResult
        {
            Title = document.Title,
            DownloadLink = payload,
            Password = password,
            Resolution = resolution,
            Codec = codec,
            HDR = hdr,
            Source = source,
            FileSizeBytes = fileSizeBytes,
            ServiceType = payload.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? DownloadServiceType.Torrent : DownloadServiceType.Hosted,
            AudioLanguages = ExtractLanguages(decodedText, true),
            AudioCodecs = audioCodecs,
            SubtitleLanguages = ExtractLanguages(decodedText, false),
            Bitrate = bitrate,
            Release = release,
            UploadedDate = document.UploadedDate
        }).ToArray();
    }

    private async Task<IEnumerable<string>> ExtractDownloadPayloadsAsync(string html, CancellationToken ct)
    {
        var fileCrypt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var affiliateLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directLinks = new List<string>();
        var availability = await ProviderAvailabilityFilter.FindOnlineLinksAsync(html, _fetcher, _logger, ct);
        var onlineLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var onlineLink in availability.OnlineLinks)
        {
            if (TryMakeAbsolute(onlineLink, out var absoluteOnlineLink))
            {
                onlineLinks.Add(NormalizeUrl(absoluteOnlineLink));
            }
        }

        foreach (Match match in LinkRegex.Matches(html))
        {
            if (!TryMakeAbsolute(WebUtility.HtmlDecode(match.Groups["url"].Value), out var absoluteUrl))
            {
                continue;
            }

            if (availability.HasIndicators && !onlineLinks.Contains(NormalizeUrl(absoluteUrl)))
            {
                continue;
            }

            if (absoluteUrl.Contains("filecrypt.cc/Container/", StringComparison.OrdinalIgnoreCase))
            {
                fileCrypt.Add(absoluteUrl);
                continue;
            }

            if (absoluteUrl.Contains("/out/af.php?", StringComparison.OrdinalIgnoreCase) ||
                absoluteUrl.Contains("/azn/af.php?", StringComparison.OrdinalIgnoreCase))
            {
                affiliateLinks.Add(absoluteUrl);
                continue;
            }

            if (!LooksLikeDownloadLink(absoluteUrl))
            {
                continue;
            }

            directLinks.Add(absoluteUrl);
        }

        if (fileCrypt.Count > 0)
        {
            return fileCrypt;
        }

        if (affiliateLinks.Count > 0)
        {
            return affiliateLinks;
        }

        if (directLinks.Count == 0)
        {
            return [];
        }

        var grouped = directLinks
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(link => new Uri(link).Host, StringComparer.OrdinalIgnoreCase)
            .Select(group => string.Join('\n', group))
            .ToArray();

        return grouped;
    }

    internal static string? ExtractPassword(string html, string decodedText)
    {
        var password = TryExtractPassword(decodedText) ?? TryExtractPassword(DecodeHtml(html));
        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        password = password
            .Trim()
            .Trim('"', '\'', ':', ';', '.', ',', '>', '-', ' ');

        return string.IsNullOrWhiteSpace(password) ? null : password;
    }

    private static string? TryExtractPassword(string text)
    {
        var match = PasswordRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["password"].Value;
        return value.Contains("Download", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    internal static string[] ExtractLanguages(string text, bool audio)
    {
        var source = text;
        if (audio)
        {
            var audioIndex = text.IndexOf("Audio", StringComparison.OrdinalIgnoreCase);
            var toneIndex = text.IndexOf("Ton", StringComparison.OrdinalIgnoreCase);
            var speechIndex = text.IndexOf("Sprache", StringComparison.OrdinalIgnoreCase);
            var start = new[] { audioIndex, toneIndex, speechIndex }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            if (start >= 0)
            {
                source = text[start..Math.Min(text.Length, start + 220)];
            }
        }
        else
        {
            var subIndex = text.IndexOf("Untertitel", StringComparison.OrdinalIgnoreCase);
            var subtitleIndex = text.IndexOf("Subtitle", StringComparison.OrdinalIgnoreCase);
            if (subIndex >= 0)
            {
                source = text[subIndex..Math.Min(text.Length, subIndex + 220)];
            }
            else if (subtitleIndex >= 0)
            {
                source = text[subtitleIndex..Math.Min(text.Length, subtitleIndex + 220)];
            }
        }

        var languages = new List<string>();
        AddIfFound(languages, source, "German", "Deutsch", "GER");
        AddIfFound(languages, source, "English", "Englisch", "ENG");
        AddIfFound(languages, source, "French", "Französisch", "FRE");
        AddIfFound(languages, source, "Spanish", "Spanisch", "SPA");
        AddIfFound(languages, source, "Italian", "Italienisch", "ITA");
        return languages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddIfFound(List<string> languages, string source, string normalized, params string[] patterns)
    {
        if (patterns.Any(pattern => source.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            languages.Add(normalized);
        }
    }

    internal static string? ParseCodec(string text)
    {
        if (text.Contains("AV1", StringComparison.OrdinalIgnoreCase))
        {
            return "AV1";
        }

        if (text.Contains("x265", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("h265", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HEVC", StringComparison.OrdinalIgnoreCase))
        {
            return "H.265";
        }

        if (text.Contains("x264", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("h264", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AVC", StringComparison.OrdinalIgnoreCase))
        {
            return "H.264";
        }

        return null;
    }

    internal static string[] ParseAudioCodecs(string text)
    {
        var codecs = new List<string>();
        AddIfFound(codecs, text, "Atmos", "Atmos");
        AddIfFound(codecs, text, "DTS-HD MA", "DTS-HD MA", "DTS HD MA");
        AddIfFound(codecs, text, "DTS-HD", "DTS-HD", "DTS HD");
        AddIfFound(codecs, text, "TrueHD", "TrueHD");
        AddIfFound(codecs, text, "DDP5.1", "DDP5.1", "DD+ 5.1", "E-AC3 5.1", "EAC3 5.1");
        AddIfFound(codecs, text, "DD5.1", "DD5.1", "DD 5.1", "AC3 5.1");
        AddIfFound(codecs, text, "AAC", "AAC");
        AddIfFound(codecs, text, "FLAC", "FLAC");
        AddIfFound(codecs, text, "MP3", "MP3");
        return codecs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? ParseHdr(string text)
    {
        if (text.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(text, @"\bDV\b", RegexOptions.IgnoreCase))
        {
            return "Dolby Vision";
        }

        if (text.Contains("HDR10+", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10+";
        }

        if (text.Contains("HDR10", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10";
        }

        if (Regex.IsMatch(text, @"\bHDR\b", RegexOptions.IgnoreCase))
        {
            return "HDR";
        }

        return null;
    }

    internal static string? ParseSource(string text)
    {
        if (text.Contains("UHD BluRay", StringComparison.OrdinalIgnoreCase))
        {
            return "UHD BluRay";
        }

        if (text.Contains("BluRay", StringComparison.OrdinalIgnoreCase))
        {
            return "BluRay";
        }

        if (text.Contains("WEB-DL", StringComparison.OrdinalIgnoreCase))
        {
            return "WEB-DL";
        }

        if (text.Contains("WEBRip", StringComparison.OrdinalIgnoreCase))
        {
            return "WEBRip";
        }

        if (text.Contains("HDTV", StringComparison.OrdinalIgnoreCase))
        {
            return "HDTV";
        }

        if (text.Contains("Remux", StringComparison.OrdinalIgnoreCase))
        {
            return "Remux";
        }

        return null;
    }

    internal static int? ParseBitrateKbps(string text)
    {
        int? best = null;
        foreach (Match match in BitrateRegex.Matches(text))
        {
            if (!double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var kbps = match.Groups["unit"].Value.ToUpperInvariant() switch
            {
                "MBPS" or "MBIT/S" or "MBIT" => (int)Math.Round(value * 1000d, MidpointRounding.AwayFromZero),
                "KBPS" or "KBIT/S" or "KBIT" => (int)Math.Round(value, MidpointRounding.AwayFromZero),
                _ => 0
            };

            if (kbps <= 0)
            {
                continue;
            }

            if (!best.HasValue || kbps > best.Value)
            {
                best = kbps;
            }
        }

        return best;
    }

    internal static string NormalizeReleaseName(string title)
    {
        return Regex.Replace(DecodeHtml(title), @"\s+", " ").Trim();
    }

    internal static long ParseEstimatedSizeBytes(string text, IReadOnlyCollection<string> payloads)
    {
        var sizes = SizeRegex.Matches(text)
            .Select(ParseSizeBytes)
            .Where(size => size > 0)
            .OrderByDescending(size => size)
            .ToArray();

        if (sizes.Length == 0)
        {
            return 0;
        }

        var partCount = EstimatePartCount(payloads);
        if (partCount <= 1 || !LooksLikeMultipart(text))
        {
            return sizes[0];
        }

        if (sizes.Length < partCount)
        {
            return sizes[0];
        }

        var leadingParts = sizes.Take(partCount).ToArray();
        var smallestPart = leadingParts.Min();
        var largestPart = leadingParts.Max();
        if (smallestPart <= 0 || (double)largestPart / smallestPart > 1.35)
        {
            return sizes[0];
        }

        return leadingParts.Sum();
    }

    private static long ParseSizeBytes(Match match)
    {
        if (!double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            "GB" or "GIB" => 1024d * 1024d * 1024d,
            "MB" or "MIB" => 1024d * 1024d,
            "KB" or "KIB" => 1024d,
            _ => 1d
        };

        return (long)(value * multiplier);
    }

    internal static int EstimatePartCount(IEnumerable<string> payloads)
    {
        return payloads
            .Select(payload => payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length)
            .DefaultIfEmpty(1)
            .Max();
    }

    internal static bool LooksLikeMultipart(string text)
    {
        return MultipartRegex.IsMatch(text);
    }

    internal static Dictionary<string, string> ExtractHdEncodeUnlockPayload(string html)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in new[]
                 {
                     "content-protector-captcha",
                     "content-protector-token",
                     "content-protector-ident",
                     "chax-response",
                     "content-protector-submit"
                 })
        {
            var match = Regex.Match(
                html,
                $@"name=""{Regex.Escape(field)}""[^>]*value=""(?<value>[^""]*)""",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                values[field] = WebUtility.HtmlDecode(match.Groups["value"].Value);
            }
        }

        return values;
    }

    internal static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal);
    }

    private static string NormalizeUrl(string url)
    {
        return url.Split('#')[0].TrimEnd('/');
    }

    private bool TryMakeAbsolute(string value, out string absoluteUrl)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            absoluteUrl = absolute.ToString();
            return true;
        }

        if (Uri.TryCreate(_baseUri, value, out var relative))
        {
            absoluteUrl = relative.ToString();
            return true;
        }

        absoluteUrl = string.Empty;
        return false;
    }

    private bool LooksLikeResultPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return false;
        }

        if (path.Contains("/wp-content/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/wp-admin/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/search/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/tag/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/category/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/genre/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/release-year/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesSearchTerm(string haystack, IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return true;
        }

        return tokens.Any(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractQueryTokens(string term)
    {
        var tokens = Regex.Split(term, @"[\s._\-:/]+")
            .Where(token => token.Length >= 4 || token.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return tokens;
    }

    internal static string ExtractHtmlTitle(string html)
    {
        var match = TitleRegex.Match(html);
        return match.Success ? DecodeHtml(StripTags(match.Groups["title"].Value)) : string.Empty;
    }

    internal static DateTime? ExtractPostedDate(string html)
    {
        var match = PostedRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        return DateTime.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static bool LooksLikeDownloadLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lowered = url.ToLowerInvariant();
        if (lowered.Contains("/wp-content/") ||
            lowered.Contains("imdb.com") ||
            lowered.Contains("youtube.com") ||
            lowered.Contains("youtu.be") ||
            lowered.Contains("pixhost.") ||
            lowered.EndsWith(".png") ||
            lowered.EndsWith(".jpg") ||
            lowered.EndsWith(".jpeg") ||
            lowered.EndsWith(".gif") ||
            lowered.EndsWith(".svg"))
        {
            return false;
        }

        return uri.Host.Contains("rapidgator", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("ddownload", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("nitroflare", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("1fichier", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("clicknupload", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("filecrypt", StringComparison.OrdinalIgnoreCase);
    }

    private static string MatchValue(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups["value"].Value : string.Empty;
    }

    private static string StripTags(string value)
    {
        return Regex.Replace(value, "<[^>]+>", " ");
    }

    private static string DecodeHtml(string? value)
    {
        return WebUtility.HtmlDecode(value ?? string.Empty).Replace('\u00A0', ' ').Trim();
    }

    private sealed record PageCandidate(string PageUrl, string? Title, string? DetailUrl);

    private sealed record PostDocument(string PageUrl, string Title, string ContentHtml, DateTime? UploadedDate);
}
