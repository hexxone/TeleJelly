using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration;

public class DownloadManagerSettings
{
    public bool Enabled { get; set; } = false;
    public string TmdbApiKey { get; set; } = "";
    public TorrentServicesSettings TorrentServices { get; set; } = new();
    public HostedServicesSettings HostedServices { get; set; } = new();
    public ExtractionSettings Extraction { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
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
    public bool AutoAddToJellyfinLibrary { get; set; } = true;
    public List<LibrarySettings> LibrarySettings { get; set; } = [];
}

public class TorrentServicesSettings
{
    public TransmissionSettings Transmission { get; set; } = new();
    public QBittorrentSettings QBittorrent { get; set; } = new();
}

public class TransmissionSettings
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 9091;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/transmission";
}

public class QBittorrentSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/qbittorrent";
}

public class HostedServicesSettings
{
    public JDownloader2Settings JDownloader2 { get; set; } = new();
    public PyLoadSettings PyLoad { get; set; } = new();
}

public class JDownloader2Settings
{
    public bool Enabled { get; set; } = false;
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/jdownloader";
    public int RetryFailedLinksMaxAttempts { get; set; } = 10;
    public int RetryFailedLinksDelayMinutes { get; set; } = 30;
}

public class PyLoadSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8000;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/pyload";
    public int RetryFailedLinksMaxAttempts { get; set; } = 10;
    public int RetryFailedLinksDelayMinutes { get; set; } = 30;
}

public class ExtractionSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> Passwords { get; set; } = ["password", "123456"];
    public bool ExtractPasswordsFromDlc { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;
}

public class SearchSettings
{
    public bool Enabled { get; set; } = false;
    public List<string> EnabledServices { get; set; } = [];
}

public class LibrarySettings
{
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string PathTemplate { get; set; } = "{title} ({year})/{title} ({year}){ext}";
    public List<DynamicPathVariable> DynamicVariables { get; set; } = [];
    public QualityProfile QualityProfile { get; set; } = new();
}

public class DynamicPathVariable
{
    public string Name { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public string? DefaultValue { get; set; }
}

public class QualityProfile
{
    public List<string> PreferredResolutions { get; set; } = ["2160p", "1080p", "720p"];

    public List<ResolutionSettings> MaxFileSizeByResolution { get; set; } = [new() { Resolution = "2160p", Bytes = 50L * 1024 * 1024 * 1024 }, new() { Resolution = "1080p", Bytes = 20L * 1024 * 1024 * 1024 }, new() { Resolution = "720p", Bytes = 10L * 1024 * 1024 * 1024 }];

    public List<ResolutionSettings> MinFileSizeByResolution { get; set; } = [new() { Resolution = "2160p", Bytes = 15L * 1024 * 1024 * 1024 }, new() { Resolution = "1080p", Bytes = 5L * 1024 * 1024 * 1024 }, new() { Resolution = "720p", Bytes = 2L * 1024 * 1024 * 1024 }];

    public List<string> RequiredAudioLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredAudioLanguages { get; set; } = ["ger", "eng"];
    public List<string> RequiredSubtitleLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredSubtitleLanguages { get; set; } = ["ger", "eng"];
    public List<string> PreferredCodecs { get; set; } = ["H.265", "H.264"];
    public List<string> PreferredHDR { get; set; } = ["Dolby Vision", "HDR10", "SDR"];
    public List<string> PreferredSources { get; set; } = ["BluRay", "WEB-DL", "WEBRip"];

    public int MinimumSeeders { get; set; } = 3;
}

public class ResolutionSettings
{
    public string Resolution { get; set; } = "";

    public long Bytes { get; set; }
}
