using System;
using System.Collections.Generic;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

internal static class DownloadFlowPresentation
{
    internal static bool ShouldAutoSelectSearchResult(IReadOnlyList<SearchResult> rankedResults)
    {
        if (rankedResults.Count == 0)
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

        parts.Add($"S{result.QualityScore:F0}");
        return parts.Count == 0 ? identity : $"{identity} [{string.Join(" | ", parts)}]";
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
        if (parts.Length < 3 || parts[0] != "dl" || !Guid.TryParse(parts[1], out downloadId))
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

    private static string FormatBytes(long bytes)
    {
        var gib = bytes / (1024d * 1024d * 1024d);
        if (gib >= 1)
        {
            return $"{gib:F1} GiB";
        }

        var mib = bytes / (1024d * 1024d);
        return $"{mib:F0} MiB";
    }
}
