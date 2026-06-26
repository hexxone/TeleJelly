using System;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public class SearchResult
{
    public string Title { get; set; } = string.Empty;
    public string DownloadLink { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? Resolution { get; set; }
    public string? Codec { get; set; }
    public string? HDR { get; set; }
    public string? Source { get; set; }
    public long FileSizeBytes { get; set; }
    public int Seeders { get; set; }
    public DownloadServiceType ServiceType { get; set; }

    public string[] AudioLanguages { get; set; } = [];
    public string[] AudioCodecs { get; set; } = [];
    public string[] SubtitleLanguages { get; set; } = [];
    public int? Bitrate { get; set; }
    public string Release { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public DateTime? UploadedDate { get; set; }
}
