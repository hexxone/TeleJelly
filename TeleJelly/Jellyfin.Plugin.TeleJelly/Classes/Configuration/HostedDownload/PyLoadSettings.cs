namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;

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
