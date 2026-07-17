namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;

public class JDownloader2Settings
{
    public bool Enabled { get; set; } = false;
    public JDownloader2ConnectionMode ConnectionMode { get; set; } = JDownloader2ConnectionMode.MyJDownloader;
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string DeviceName { get; set; } = "";
    /// <summary>
    ///     when in same "docker network" -> can use container "hostname" as url & don't need port exposed on host
    /// </summary>
    public string LocalApiBaseUrl { get; set; } = "http://jdownloader2:3128";
    public string StagingPath { get; set; } = "/downloads/staging/jdownloader";
    public int RetryFailedLinksMaxAttempts { get; set; } = 10;
    public int RetryFailedLinksDelayMinutes { get; set; } = 30;
}
