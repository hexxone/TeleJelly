namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;

public class TorrentServicesSettings
{
    public TransmissionSettings Transmission { get; set; } = new();
    public QBittorrentSettings QBittorrent { get; set; } = new();
}
