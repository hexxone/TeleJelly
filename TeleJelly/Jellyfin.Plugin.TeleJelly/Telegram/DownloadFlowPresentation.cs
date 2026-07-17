using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

internal static class DownloadFlowPresentation
{
    private const int MaxCallbackDataBytes = 64;

    internal static string CreateLibraryCallbackData(Guid downloadId, Guid libraryId)
    {
        var callbackData = $"dl_{EncodeGuid(downloadId)}_library_{EncodeGuid(libraryId)}";
        if (Encoding.UTF8.GetByteCount(callbackData) > MaxCallbackDataBytes)
        {
            throw new InvalidOperationException("Telegram callback data exceeds the 64-byte limit.");
        }

        return callbackData;
    }

    internal static string CreateLibraryCallbackData(Guid downloadId, string libraryId)
    {
        if (!Guid.TryParse(libraryId, out var parsedLibraryId))
        {
            throw new ArgumentException("The Jellyfin library ID is not a valid GUID.", nameof(libraryId));
        }

        return CreateLibraryCallbackData(downloadId, parsedLibraryId);
    }

    internal static bool TryParseLibraryCallbackValue(string? value, out Guid libraryId)
    {
        return TryParseGuid(value, out libraryId);
    }

    internal static bool ShouldAutoSelectSearchResult(IReadOnlyList<SearchResult> rankedResults)
    {
        if (rankedResults.Count == 0)
        {
            return false;
        }

        if (rankedResults[0].QualityFallback)
        {
            return false;
        }

        if (rankedResults.Count == 1)
        {
            return true;
        }

        var best = rankedResults[0];
        var second = rankedResults[1];
        return best.QualityScore >= 1000 &&
               best.QualityScore - second.QualityScore >= 250 &&
               best.QualityScore >= second.QualityScore * 1.15;
    }

    internal static string BuildSearchResultLabel(SearchResult result)
    {
        var identity = !string.IsNullOrWhiteSpace(result.Release)
            ? result.Release
            : result.Title;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Resolution))
        {
            parts.Add(result.Resolution);
        }

        if (!string.IsNullOrWhiteSpace(result.Source))
        {
            parts.Add(result.Source);
        }

        if (result.FileSizeBytes > 0)
        {
            parts.Add(FormatBytes(result.FileSizeBytes));
        }

        if (result.QualityFallback)
        {
            parts.Add("quality fallback");
        }

        parts.Add($"S{result.QualityScore:F0}");
        return parts.Count == 0 ? identity : $"{identity} [{string.Join(" | ", parts)}]";
    }

    internal static string BuildSearchResultsMessage(
        string title,
        IReadOnlyList<SearchResult> rankedResults)
    {
        var builder = new StringBuilder();
        builder.Append("<b>Top Search Results</b>\n");
        builder.Append("Select one result for <b>")
            .Append(WebUtility.HtmlEncode(title))
            .Append("</b>:");

        if (rankedResults.Count > 0 && rankedResults[0].QualityFallback)
        {
            builder.Append("\n\n⚠️ None met every strict quality rule. Showing the best fallbacks for manual selection.");
        }

        for (var index = 0; index < rankedResults.Count; index++)
        {
            var result = rankedResults[index];
            var identity = !string.IsNullOrWhiteSpace(result.Release) ? result.Release : result.Title;
            builder.Append("\n\n<b>")
                .Append(index + 1)
                .Append(". ")
                .Append(WebUtility.HtmlEncode(identity))
                .Append("</b>");

            var summary = new List<string>
            {
                $"Score {result.QualityScore:F0}"
            };
            if (!string.IsNullOrWhiteSpace(result.Provider))
            {
                summary.Add(result.Provider);
            }

            if (result.FileSizeBytes > 0)
            {
                summary.Add(FormatBytes(result.FileSizeBytes));
            }

            if (result.Seeders > 0)
            {
                summary.Add($"{result.Seeders} seeders");
            }

            builder.Append("\n<b>Rank:</b> ")
                .Append(string.Join(" · ", summary.Select(WebUtility.HtmlEncode)));

            var video = new[]
                {
                    result.Resolution,
                    result.Source,
                    result.Codec,
                    result.HDR,
                    result.Bitrate is > 0 ? FormatBitrate(result.Bitrate.Value) : null
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (video.Length > 0)
            {
                builder.Append("\n<b>Video:</b> ")
                    .Append(string.Join(" · ", video.Select(WebUtility.HtmlEncode)));
            }

            var audio = result.AudioLanguages
                .Concat(result.AudioCodecs)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (audio.Length > 0)
            {
                builder.Append("\n<b>Audio:</b> ")
                    .Append(WebUtility.HtmlEncode(string.Join(", ", audio)));
            }

            if (result.SubtitleLanguages.Length > 0)
            {
                builder.Append("\n<b>Subtitles:</b> ")
                    .Append(WebUtility.HtmlEncode(string.Join(", ", result.SubtitleLanguages)));
            }

            if (result.QualityFallback)
            {
                builder.Append("\n<i>Quality fallback</i>");
            }
        }

        return builder.ToString();
    }

    internal static bool TryParseDownloadCallback(string? callbackData, out Guid downloadId, out string? action, out string? value)
    {
        downloadId = Guid.Empty;
        action = null;
        value = null;

        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return false;
        }

        var parts = callbackData.Split('_');
        if (parts.Length < 3 || parts[0] != "dl" || !TryParseGuid(parts[1], out downloadId))
        {
            return false;
        }

        action = parts[2];
        value = parts.Length > 3 ? parts[3] : null;
        return true;
    }

    internal static bool TryParsePathVariableSelection(Guid downloadId, string? callbackData, out string? name, out string? selectedValue)
    {
        name = null;
        selectedValue = null;

        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return false;
        }

        var pathVarPrefix = $"dl_{downloadId}_pathvar_";
        if (!callbackData.StartsWith(pathVarPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encodedPayload = callbackData[pathVarPrefix.Length..];
        var separatorIndex = encodedPayload.IndexOf('_');
        if (separatorIndex < 1 || separatorIndex + 1 >= encodedPayload.Length)
        {
            return false;
        }

        name = Uri.UnescapeDataString(encodedPayload[..separatorIndex]);
        selectedValue = Uri.UnescapeDataString(encodedPayload[(separatorIndex + 1)..]);
        return true;
    }

    internal static bool TryParseDownloadFileCaption(string? caption, string? botUsername, out string? imdbId)
    {
        imdbId = null;
        if (string.IsNullOrWhiteSpace(caption))
        {
            return false;
        }

        var parts = caption.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].StartsWith("/download", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commandParts = parts[0][1..].Split('@', 2);
        if (!string.Equals(commandParts[0], "download", StringComparison.OrdinalIgnoreCase) ||
            (commandParts.Length == 2 && !string.Equals(commandParts[1], botUsername, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!parts[1].StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Length <= 2 ||
            !parts[1][2..].All(char.IsDigit))
        {
            return false;
        }

        imdbId = parts[1];
        return true;
    }

    internal static bool TryParseManualDownloadSource(string? text, out string? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (candidate.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            source = candidate;
            return true;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        source = candidate;
        return true;
    }

    private static string FormatBytes(long bytes)
    {
        var gib = bytes / (1024d * 1024d * 1024d);
        if (gib >= 1)
        {
            return gib.ToString("F1", CultureInfo.InvariantCulture) + " GiB";
        }

        var mib = bytes / (1024d * 1024d);
        return mib.ToString("F0", CultureInfo.InvariantCulture) + " MiB";
    }

    private static string FormatBitrate(int bitrateKbps)
    {
        return bitrateKbps >= 1000
            ? (bitrateKbps / 1000d).ToString("F1", CultureInfo.InvariantCulture) + " Mbps"
            : bitrateKbps.ToString(CultureInfo.InvariantCulture) + " kbps";
    }

    private static string EncodeGuid(Guid value)
    {
        return Convert.ToBase64String(value.ToByteArray()).TrimEnd('=');
    }

    private static bool TryParseGuid(string? value, out Guid result)
    {
        if (Guid.TryParse(value, out result))
        {
            return true;
        }

        if (value?.Length != 22)
        {
            result = Guid.Empty;
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(value + "==");
            result = new Guid(bytes);
            return true;
        }
        catch (FormatException)
        {
            result = Guid.Empty;
            return false;
        }
    }
}
