using System;
using System.Collections.Generic;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;

namespace Jellyfin.Plugin.TeleJelly.Classes.Models;

public class ManagedDownload
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? LinkOrMagnet { get; set; }
    public string? SourcePassword { get; set; }
    public DownloadStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string ImdbId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public MediaType MediaType { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string? ServiceDownloadId { get; set; }
    public string? ServiceName { get; set; }
    public DownloadServiceType ServiceType { get; set; }
    public double ProgressPercentage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastStatusChangeAt { get; set; }
    public DateTime? LastProgressAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? OriginalDownloadPath { get; set; }
    public string? CurrentStagingPath { get; set; }
    public string? SuggestedDestinationPath { get; set; }
    public string? UserConfirmedPath { get; set; }
    public string? TargetLibraryId { get; set; }
    public bool RequiresExtraction { get; set; }
    public string[]? TriedPasswords { get; set; }
    public Dictionary<string, string>? PendingPathVariables { get; set; }
    public Dictionary<string, string>? FilledPathVariables { get; set; }
    public SearchResult[]? SearchResults { get; set; }
    public MediaFileGroup[]? AnalyzedFiles { get; set; }
    public int StartAttempts { get; set; }
}

public enum DownloadServiceType
{
    Torrent,
    Hosted
}
