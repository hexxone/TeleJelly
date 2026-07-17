using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal static class ProviderAvailabilityFilter
{
    // FileCrypt's green status badge, verified against the provider's documented
    // status-online legend. The status endpoint returns one of the four tiny PNGs.
    internal const string OnlineFileCryptBadgeSha256 = "187f352d5f99e7fd3e5e15c8c3607003b40ff6fbcfbee7067df01a756cf4d624";

    private static readonly Regex AnchorRegex = new(
        @"<a\b[^>]*\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<body>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StatusImageRegex = new(
        @"https?://filecrypt\.cc/Stat/[^\s""'<>]+\.png",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExplicitStatusAttributeRegex = new(
        @"(?:(?:data-status|data-availability)\s*=\s*[""'][^""']*\b(?<status>online|available|green|partial|offline|unavailable|unknown|red)\b[^""']*[""']|(?:class|id)\s*=\s*[""'][^""']*\b(?:status|availability|download|link)[-_ ]+(?<namedStatus>online|available|green|partial|offline|unavailable|unknown|red)\b[^""']*[""'])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal sealed record AvailabilityResult(bool HasIndicators, IReadOnlySet<string> OnlineLinks);

    internal static async Task<AvailabilityResult> FindOnlineLinksAsync(
        string html,
        ISearchDocumentFetcher fetcher,
        ILogger logger,
        CancellationToken ct)
    {
        var onlineLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasIndicators = false;

        foreach (Match anchor in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(anchor.Groups["href"].Value).Trim();
            var anchorHtml = anchor.Value;
            var staticStatus = ClassifyStaticStatus(anchorHtml);
            if (staticStatus.HasValue)
            {
                hasIndicators = true;
                if (staticStatus.Value)
                {
                    onlineLinks.Add(href);
                }

                continue;
            }

            var statusImage = StatusImageRegex.Match(anchorHtml);
            if (!statusImage.Success)
            {
                continue;
            }

            hasIndicators = true;
            try
            {
                var imageBytes = await fetcher.GetBytesAsync(new Uri(statusImage.Value, UriKind.Absolute), ct);
                if (IsOnlineFileCryptBadge(imageBytes))
                {
                    onlineLinks.Add(href);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A status badge that cannot be verified is not green. This is deliberately
                // fail-closed because unknown FileCrypt badges can represent deleted 404 folders.
                logger.LogWarning(ex, "Could not verify provider availability badge {StatusImage}", statusImage.Value);
            }
        }

        return new AvailabilityResult(hasIndicators, onlineLinks);
    }

    internal static bool IsOnlineFileCryptBadge(byte[] imageBytes)
    {
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(imageBytes)),
            OnlineFileCryptBadgeSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ClassifyStaticStatus(string anchorHtml)
    {
        if (anchorHtml.Contains("status-online", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (anchorHtml.Contains("status-partial", StringComparison.OrdinalIgnoreCase) ||
            anchorHtml.Contains("status-offline", StringComparison.OrdinalIgnoreCase) ||
            anchorHtml.Contains("status-unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var statusAttribute = ExplicitStatusAttributeRegex.Match(anchorHtml);
        if (!statusAttribute.Success)
        {
            return null;
        }

        var status = statusAttribute.Groups["status"].Success
            ? statusAttribute.Groups["status"].Value
            : statusAttribute.Groups["namedStatus"].Value;
        return status.ToLowerInvariant() switch
        {
            "online" or "available" or "green" => true,
            "partial" or "offline" or "unavailable" or "unknown" or "red" => false,
            _ => null
        };
    }
}
