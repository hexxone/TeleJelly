using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public class SearchResult
{
    public string Title { get; set; } = string.Empty;
    public string DownloadLink { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? Codec { get; set; }
    public string? HDR { get; set; }
    public string? Source { get; set; }
    public long FileSizeBytes { get; set; }
    public int Seeders { get; set; }
    public DownloadServiceType ServiceType { get; set; }
}
