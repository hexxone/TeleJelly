using System;
using System.Globalization;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal static class DownloadWorkflowPolicies
{
    internal static bool TryUpdateProgress(ManagedDownload download, double progressPercentage, DateTime nowUtc)
    {
        if (progressPercentage < 0)
        {
            return false;
        }

        if (Math.Abs(download.ProgressPercentage - progressPercentage) < 0.01)
        {
            return false;
        }

        download.ProgressPercentage = progressPercentage;
        if (progressPercentage > 0)
        {
            download.LastProgressAt = nowUtc;
        }

        return true;
    }

    internal static bool HasDownloadTimedOut(ManagedDownload download, DownloadManagerSettings? config, DateTime nowUtc, out string? reason)
    {
        reason = null;

        if (config == null)
        {
            return false;
        }

        if (config.DownloadTimeoutMinutes > 0 &&
            nowUtc - download.StartedAt > TimeSpan.FromMinutes(config.DownloadTimeoutMinutes))
        {
            reason = $"Download exceeded timeout of {config.DownloadTimeoutMinutes} minutes.";
            return true;
        }

        var progressAnchor = download.LastProgressAt ?? download.StartedAt;
        if (config.StalledNoProgressTimeoutMinutes > 0 &&
            nowUtc - progressAnchor > TimeSpan.FromMinutes(config.StalledNoProgressTimeoutMinutes))
        {
            reason = $"No download progress for {config.StalledNoProgressTimeoutMinutes} minutes.";
            return true;
        }

        return false;
    }

    internal static bool HasTorrentAvailabilityTimedOut(ManagedDownload download, object progress, DownloadManagerSettings? config, DateTime nowUtc, out string? reason)
    {
        reason = null;

        if (config == null || config.StalledNoSeedsTimeoutMinutes <= 0 || download.ProgressPercentage >= 100)
        {
            return false;
        }

        var seeders = ReadIntProperty(progress, "Seeders", "SeederCount", "NumSeeds", "NumComplete");
        var peers = ReadIntProperty(progress, "Leechers", "PeersConnected", "NumLeechs", "PeersGettingFromUs", "PeersSendingToUs");
        var speed = ReadDoubleProperty(progress, "RateDownload", "DlSpeed", "DownloadSpeed", "Dlspeed");
        var state = ReadStringProperty(progress, "Status", "State");
        var hasAvailabilitySignal = seeders.HasValue || peers.HasValue || speed.HasValue || !string.IsNullOrWhiteSpace(state);

        if (!hasAvailabilitySignal)
        {
            return false;
        }

        if ((seeders ?? 0) > 0 || (peers ?? 0) > 0 || (speed ?? 0) > 0.1)
        {
            return false;
        }

        var availabilityAnchor = download.LastProgressAt ?? download.StartedAt;
        if (nowUtc - availabilityAnchor <= TimeSpan.FromMinutes(config.StalledNoSeedsTimeoutMinutes))
        {
            return false;
        }

        reason = string.IsNullOrWhiteSpace(state)
            ? $"No seeds or peers reported for {config.StalledNoSeedsTimeoutMinutes} minutes."
            : $"Torrent remained in '{state}' without seeds or peers for {config.StalledNoSeedsTimeoutMinutes} minutes.";
        return true;
    }

    internal static bool IsValidTransition(DownloadStatus from, DownloadStatus to)
    {
        if (from == to)
        {
            return true;
        }

        if (to is DownloadStatus.Failed or DownloadStatus.Canceled)
        {
            return true;
        }

        return from switch
        {
            DownloadStatus.Pending => to == DownloadStatus.Downloading,
            DownloadStatus.AwaitingLibrary => to == DownloadStatus.AwaitingMediaType,
            DownloadStatus.AwaitingMediaType => to is DownloadStatus.AwaitingSeason or DownloadStatus.AwaitingSearchResult or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingSeason => to is DownloadStatus.AwaitingSearchResult or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingSearchResult => to is DownloadStatus.AwaitingPathVars or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingPathVars => to is DownloadStatus.AwaitingPathVars or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingPathConfirm => to is DownloadStatus.Downloading or DownloadStatus.Pending,
            DownloadStatus.Downloading => to is DownloadStatus.Extracting or DownloadStatus.Stalled,
            DownloadStatus.Extracting => to is DownloadStatus.Analyzing or DownloadStatus.ExtractionFailed,
            DownloadStatus.ExtractionFailed => to == DownloadStatus.Extracting,
            DownloadStatus.Analyzing => to == DownloadStatus.Organizing,
            DownloadStatus.Organizing => to == DownloadStatus.Completed,
            DownloadStatus.Stalled => to is DownloadStatus.Downloading or DownloadStatus.Extracting,
            DownloadStatus.Failed => to is DownloadStatus.AwaitingPathConfirm or DownloadStatus.Extracting,
            DownloadStatus.Canceled => to == DownloadStatus.AwaitingPathConfirm,
            _ => false
        };
    }

    private static int? ReadIntProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = source.GetType().GetProperty(propertyName);
            var value = property?.GetValue(source);
            if (value == null)
            {
                continue;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // Ignore backend-specific fields we cannot coerce.
            }
        }

        return null;
    }

    private static double? ReadDoubleProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = source.GetType().GetProperty(propertyName);
            var value = property?.GetValue(source);
            if (value == null)
            {
                continue;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // Ignore backend-specific fields we cannot coerce.
            }
        }

        return null;
    }

    private static string? ReadStringProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = source.GetType().GetProperty(propertyName);
            if (property?.GetValue(source) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
