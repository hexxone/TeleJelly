using System;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class DownloadWorkflowPoliciesTests
{
    [Test]
    public void TryUpdateProgress_UpdatesTimestampOnlyForMeaningfulChanges()
    {
        var download = CreateDownload();
        var now = DateTime.UtcNow;

        var changed = DownloadWorkflowPolicies.TryUpdateProgress(download, 42.0, now);
        var unchanged = DownloadWorkflowPolicies.TryUpdateProgress(download, 42.005, now.AddMinutes(1));

        Assert.That(changed, Is.True);
        Assert.That(download.ProgressPercentage, Is.EqualTo(42.0).Within(0.001));
        Assert.That(download.LastProgressAt, Is.EqualTo(now));
        Assert.That(unchanged, Is.False);
    }

    [Test]
    public void HasDownloadTimedOut_ReturnsTotalTimeoutReason()
    {
        var config = new DownloadManagerSettings
        {
            DownloadTimeoutMinutes = 60,
            StalledNoProgressTimeoutMinutes = 30
        };
        var download = CreateDownload(startedAt: DateTime.UtcNow.AddHours(-2), lastProgressAt: DateTime.UtcNow.AddMinutes(-5));

        var timedOut = DownloadWorkflowPolicies.HasDownloadTimedOut(download, config, DateTime.UtcNow, out var reason);

        Assert.That(timedOut, Is.True);
        Assert.That(reason, Does.Contain("exceeded timeout"));
    }

    [Test]
    public void HasDownloadTimedOut_UsesLastProgressAnchorForStallDetection()
    {
        var config = new DownloadManagerSettings
        {
            DownloadTimeoutMinutes = 0,
            StalledNoProgressTimeoutMinutes = 30
        };
        var download = CreateDownload(startedAt: DateTime.UtcNow.AddHours(-5), lastProgressAt: DateTime.UtcNow.AddMinutes(-45));

        var timedOut = DownloadWorkflowPolicies.HasDownloadTimedOut(download, config, DateTime.UtcNow, out var reason);

        Assert.That(timedOut, Is.True);
        Assert.That(reason, Does.Contain("No download progress"));
    }

    [Test]
    public void HasTorrentAvailabilityTimedOut_TriggersWhenNoSeedsPeersOrSpeedRemain()
    {
        var config = new DownloadManagerSettings
        {
            StalledNoSeedsTimeoutMinutes = 20
        };
        var download = CreateDownload(progress: 50, startedAt: DateTime.UtcNow.AddHours(-1), lastProgressAt: DateTime.UtcNow.AddMinutes(-30));
        var progress = new { Seeders = 0, PeersConnected = 0, RateDownload = 0.0, Status = "Metadata" };

        var stalled = DownloadWorkflowPolicies.HasTorrentAvailabilityTimedOut(download, progress, config, DateTime.UtcNow, out var reason);

        Assert.That(stalled, Is.True);
        Assert.That(reason, Does.Contain("without seeds or peers"));
    }

    [Test]
    public void HasTorrentAvailabilityTimedOut_IgnoresHealthyAvailabilitySignals()
    {
        var config = new DownloadManagerSettings
        {
            StalledNoSeedsTimeoutMinutes = 20
        };
        var download = CreateDownload(progress: 50, startedAt: DateTime.UtcNow.AddHours(-1), lastProgressAt: DateTime.UtcNow.AddMinutes(-30));
        var progress = new { Seeders = 2, PeersConnected = 0, RateDownload = 0.0, Status = "Downloading" };

        var stalled = DownloadWorkflowPolicies.HasTorrentAvailabilityTimedOut(download, progress, config, DateTime.UtcNow, out var reason);

        Assert.That(stalled, Is.False);
        Assert.That(reason, Is.Null);
    }

    [TestCase(DownloadStatus.AwaitingPathConfirm, DownloadStatus.Pending, true)]
    [TestCase(DownloadStatus.Extracting, DownloadStatus.ExtractionFailed, true)]
    [TestCase(DownloadStatus.Completed, DownloadStatus.Downloading, false)]
    [TestCase(DownloadStatus.Canceled, DownloadStatus.AwaitingPathConfirm, true)]
    public void IsValidTransition_TracksLifecycleRules(DownloadStatus from, DownloadStatus to, bool expected)
    {
        Assert.That(DownloadWorkflowPolicies.IsValidTransition(from, to), Is.EqualTo(expected));
    }

    private static ManagedDownload CreateDownload(
        double progress = 0,
        DateTime? startedAt = null,
        DateTime? lastProgressAt = null)
    {
        return new ManagedDownload
        {
            Id = Guid.NewGuid(),
            Title = "Example",
            ImdbId = "tt1234567",
            Status = DownloadStatus.Downloading,
            ServiceType = DownloadServiceType.Torrent,
            ProgressPercentage = progress,
            StartedAt = startedAt ?? DateTime.UtcNow.AddMinutes(-10),
            LastProgressAt = lastProgressAt
        };
    }
}
