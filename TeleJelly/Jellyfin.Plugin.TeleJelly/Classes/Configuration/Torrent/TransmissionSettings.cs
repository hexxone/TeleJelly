namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;

public class TransmissionSettings
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "transmission";
    public int Port { get; set; } = 9091;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StagingPath { get; set; } = "/downloads/staging/transmission";
}
