using System;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal static class DownloadFailureGuidance
{
    private const string GuidanceMarker = "You can try to manually run";
    private const string ReplyGuidanceMarker = "reply to this message";

    internal static string Append(ManagedDownload download, string reason)
    {
        return Append(reason, download.ImdbId, download.LinkOrMagnet, download.SourcePassword);
    }

    internal static string Append(string reason, string imdbId, string? source = null, string? password = null)
    {
        if (reason.Contains(GuidanceMarker, StringComparison.Ordinal))
        {
            return reason;
        }

        var publicSource = IsPublicSource(source) ? source!.Trim() : null;
        var commandSource = publicSource ?? "<url>";
        var builder = new StringBuilder(reason.Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"You can try to manually run `/download {imdbId} {commandSource}`");
        builder.Append(CultureInfo.InvariantCulture, $"or attach a `.torrent` or `.dlc` file with caption `/download {imdbId}`.");

        if (publicSource != null)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"Source: {publicSource}");
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture, $"Password: {password.Trim()}");
        }

        return builder.ToString();
    }

    internal static string AppendReplyOption(string reason)
    {
        if (reason.Contains(ReplyGuidanceMarker, StringComparison.OrdinalIgnoreCase))
        {
            return reason;
        }

        return $"{reason.Trim()}\n\nYou can reply to this message with a URL, magnet link, `.torrent`, or `.dlc` file to retry this download.";
    }

    private static bool IsPublicSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
               (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https");
    }
}
