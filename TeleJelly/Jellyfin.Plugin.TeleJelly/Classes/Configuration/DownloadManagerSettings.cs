using System.Collections.Generic;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration;

public class DownloadManagerSettings
{
    public bool Enabled { get; set; } = false;
    public string TmdbApiKey { get; set; } = "";
    public TorrentServicesSettings TorrentServices { get; set; } = new();
    public HostedServicesSettings HostedServices { get; set; } = new();
    public ExtractionSettings Extraction { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public HealthMonitoringSettings HealthMonitoring { get; set; } = new();
    public long MaxDownloadSizeBytes { get; set; } = 100L * 1024 * 1024 * 1024; // 100 GiB
    public int DownloadTimeoutMinutes { get; set; } = 120; // 2 hours
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int StalledNoSeedsTimeoutMinutes { get; set; } = 60;
    public int StalledNoProgressTimeoutMinutes { get; set; } = 30;
    public bool AutoRemoveCompletedAfterDays { get; set; } = true;
    public int AutoRemoveCompletedDays { get; set; } = 7;
    public bool AutoRemoveFailedAfterDays { get; set; } = true;
    public int AutoRemoveFailedDays { get; set; } = 3;
    public List<string> WhitelistUsernames { get; set; } = [];
    public bool TriggerLibraryScanAfterOrganize { get; set; } = true;
    public List<LibrarySettings> LibrarySettings { get; set; } = [];
}
