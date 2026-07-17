using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

internal static class SearchTitleMatcher
{
    public static bool IsMatch(SearchResult result, IEnumerable<string> expectedTitles, string? imdbId)
    {
        var haystack = Normalize($"{result.Title} {result.Release}");
        if (haystack.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(imdbId) &&
            haystack.Contains(Normalize(imdbId), StringComparison.Ordinal))
        {
            return true;
        }

        return expectedTitles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(Normalize)
            .Where(title => title.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Any(title => IsAliasMatch(title, haystack));
    }

    private static bool IsAliasMatch(string alias, string haystack)
    {
        var paddedHaystack = $" {haystack} ";
        if (paddedHaystack.Contains($" {alias} ", StringComparison.Ordinal))
        {
            return true;
        }

        var aliasTokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (aliasTokens.Length < 2)
        {
            return false;
        }

        var haystackTokens = haystack
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var matchedTokens = aliasTokens.Count(haystackTokens.Contains);
        var requiredMatches = Math.Max(2, (int)Math.Ceiling(aliasTokens.Length * 0.75d));

        return matchedTokens >= requiredMatches && aliasTokens.Any(token => token.Length >= 4 && haystackTokens.Contains(token));
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        var previousWasSeparator = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                normalized.Append(' ');
                previousWasSeparator = true;
            }
        }

        return normalized.ToString().Trim();
    }
}
