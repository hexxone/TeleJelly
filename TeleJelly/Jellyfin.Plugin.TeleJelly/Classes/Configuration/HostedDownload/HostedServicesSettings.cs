namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;

public class HostedServicesSettings
{
    public JDownloader2Settings JDownloader2 { get; set; } = new();
    public PyLoadSettings PyLoad { get; set; } = new();
}
