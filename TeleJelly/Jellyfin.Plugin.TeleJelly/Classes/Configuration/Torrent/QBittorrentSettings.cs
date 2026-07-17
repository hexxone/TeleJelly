namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;

public class QBittorrentSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/qbittorrent";
}