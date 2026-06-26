using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

public interface IDownloadOrchestrator
{
    Task<ManagedDownload> BeginDownloadWorkflow(string imdbId, long chatId, long userId, string? link = null);
    Task UpdateDownloadStatus(Guid id, DownloadStatus newStatus, string? errorMessage = null);
    Task<bool> CancelDownloadAsync(Guid id, CancellationToken ct);
    Task<bool> RetryDownloadAsync(Guid id, CancellationToken ct);
    Task<bool> RemoveDownloadAsync(Guid id, bool deleteFiles, CancellationToken ct);
    ManagedDownload? GetDownload(Guid id);
    IEnumerable<ManagedDownload> GetAllDownloads();
    Task ProcessAllDownloadsAsync(CancellationToken stoppingToken);
    Task<bool> InitiateDownloadAsync(Guid downloadId, CancellationToken ct);
    Task RestoreDownloadsAsync(CancellationToken ct);
}

/// <summary>
/// Coordinates download lifecycle state, persistence, recovery and final organization across all supported backends.
/// </summary>
internal sealed class DownloadOrchestrator : IDownloadOrchestrator
{
    private readonly ArchiveExtractionService _archiveExtractor;
    private readonly IServerConfigurationManager _configurationManager;

    private readonly ConcurrentDictionary<Guid, ManagedDownload> _downloads = new();
    private readonly ConcurrentDictionary<Guid, byte> _processingDownloads = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _downloadLocks = new();
    private readonly MediaFileOrganizerService _fileOrganizer;
    private readonly IEnumerable<IHostedDownloadService> _hostedServices;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<DownloadOrchestrator> _logger;
    private readonly MediaAnalyzerService _mediaAnalyzer;
    private readonly PathTemplateService _pathTemplater;
    private readonly string _persistencePath;
    private readonly IEnumerable<ITorrentDownloadService> _torrentServices;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public DownloadOrchestrator(
        ILogger<DownloadOrchestrator> logger,
        IEnumerable<ITorrentDownloadService> torrentServices,
        IEnumerable<IHostedDownloadService> hostedServices,
        ArchiveExtractionService archiveExtractor,
        MediaAnalyzerService mediaAnalyzer,
        PathTemplateService pathTemplater,
        MediaFileOrganizerService fileOrganizer,
        ILibraryManager libraryManager,
        IServerConfigurationManager configurationManager,
        IServiceHealthMonitor healthMonitor)
    {
        _logger = logger;
        _torrentServices = torrentServices;
        _hostedServices = hostedServices;
        _archiveExtractor = archiveExtractor;
        _mediaAnalyzer = mediaAnalyzer;
        _pathTemplater = pathTemplater;
        _fileOrganizer = fileOrganizer;
        _libraryManager = libraryManager;
        _configurationManager = configurationManager;
        _healthMonitor = healthMonitor;
        _persistencePath = Path.Combine(_configurationManager.ApplicationPaths.DataPath, "TeleJelly_Downloads.json");
    }

    public async Task<ManagedDownload> BeginDownloadWorkflow(string imdbId, long chatId, long userId, string? link = null)
    {
        if (TeleJellyPlugin.Instance?.Configuration.DownloadManager.Enabled != true)
        {
            throw new InvalidOperationException("Download manager is disabled.");
        }

        var metadata = await _mediaAnalyzer.GetMetadataFromImdbId(imdbId);
        if (metadata.MediaType == MediaType.Unknown || metadata.Title == null)
        {
            throw new Exception($"Could not find valid metadata for IMDB ID: {imdbId}");
        }

        var download = new ManagedDownload
        {
            Id = Guid.NewGuid(),
            ImdbId = imdbId,
            Title = metadata.Title,
            Year = metadata.Year,
            MediaType = metadata.MediaType,
            ChatId = chatId,
            UserId = userId.ToString(CultureInfo.InvariantCulture),
            LinkOrMagnet = link,
            Status = DownloadStatus.AwaitingLibrary,
            StartedAt = DateTime.UtcNow,
            LastStatusChangeAt = DateTime.UtcNow
        };

        _downloads.TryAdd(download.Id, download);
        await SaveDownloadsAsync();

        _logger.LogInformation("Beginning new download workflow for {Title} ({Year})", download.Title, download.Year);
        return download;
    }

    public async Task UpdateDownloadStatus(Guid id, DownloadStatus newStatus, string? errorMessage = null)
    {
        await WithDownloadLockAsync(id, async () =>
        {
            if (!_downloads.TryGetValue(id, out var download))
            {
                return;
            }

            if (!IsValidTransition(download.Status, newStatus))
            {
                _logger.LogDebug("Ignoring invalid state transition for {DownloadId}: {From} -> {To}", id, download.Status, newStatus);
                return;
            }

            var previousStatus = download.Status;
            download.Status = newStatus;
            download.LastStatusChangeAt = DateTime.UtcNow;
            download.ErrorMessage = errorMessage;
            if (newStatus is DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Failed)
            {
                download.CompletedAt ??= DateTime.UtcNow;
            }

            _logger.LogInformation(
                "Download {DownloadId} ({Title}) transitioned {From} -> {To}{ErrorSuffix}",
                download.Id,
                download.Title,
                previousStatus,
                newStatus,
                string.IsNullOrWhiteSpace(errorMessage) ? string.Empty : $": {errorMessage}");

            await SaveDownloadsAsync();
        });
    }

    public ManagedDownload? GetDownload(Guid id)
    {
        return _downloads.GetValueOrDefault(id);
    }

    public async Task<bool> CancelDownloadAsync(Guid id, CancellationToken ct)
    {
        if (!_downloads.TryGetValue(id, out var download))
        {
            return false;
        }

        await WithDownloadLockAsync(id, async () =>
        {
            _logger.LogInformation("Cancel requested for download {DownloadId} ({Title})", download.Id, download.Title);
            await RemoveFromBackendAsync(download, false, ct);
            download.ErrorMessage = null;
            download.CompletedAt = DateTime.UtcNow;
            download.Status = DownloadStatus.Canceled;
            await SaveDownloadsAsync();
        });

        return true;
    }

    public async Task<bool> RetryDownloadAsync(Guid id, CancellationToken ct)
    {
        if (!_downloads.TryGetValue(id, out var download))
        {
            return false;
        }

        var shouldInitiateDownload = false;

        await WithDownloadLockAsync(id, async () =>
        {
            _logger.LogInformation("Retry requested for download {DownloadId} ({Title}) from state {Status}", download.Id, download.Title, download.Status);
            download.ErrorMessage = null;
            download.CompletedAt = null;

            if (download.Status == DownloadStatus.ExtractionFailed)
            {
                download.Status = DownloadStatus.Extracting;
                download.LastStatusChangeAt = DateTime.UtcNow;
                await SaveDownloadsAsync();
                return;
            }

            await RemoveFromBackendAsync(download, false, ct);

            download.ProgressPercentage = 0;
            download.LastProgressAt = null;
            download.OriginalDownloadPath = null;
            download.CurrentStagingPath = null;
            download.RequiresExtraction = false;
            download.TriedPasswords = null;
            download.AnalyzedFiles = null;
            download.ServiceDownloadId = null;
            download.ServiceName = null;
            download.StartAttempts = 0;
            download.Status = DownloadStatus.AwaitingPathConfirm;
            download.LastStatusChangeAt = DateTime.UtcNow;
            shouldInitiateDownload = !string.IsNullOrWhiteSpace(download.LinkOrMagnet);

            await SaveDownloadsAsync();
        });

        if (shouldInitiateDownload)
        {
            return await InitiateDownloadAsync(id, ct);
        }

        return true;
    }

    public async Task<bool> RemoveDownloadAsync(Guid id, bool deleteFiles, CancellationToken ct)
    {
        if (!_downloads.TryGetValue(id, out var download))
        {
            return false;
        }

        await WithDownloadLockAsync(id, async () =>
        {
            _logger.LogInformation(
                "Removing download {DownloadId} ({Title}), deleteFiles={DeleteFiles}",
                download.Id,
                download.Title,
                deleteFiles);
            await RemoveFromBackendAsync(download, deleteFiles, ct);
            if (deleteFiles)
            {
                CleanupManagedFiles(download);
            }

            _downloads.TryRemove(id, out _);
            _processingDownloads.TryRemove(id, out _);
            await SaveDownloadsAsync();
        });

        return true;
    }

    public IEnumerable<ManagedDownload> GetAllDownloads()
    {
        return _downloads.Values;
    }

    public async Task ProcessAllDownloadsAsync(CancellationToken stoppingToken)
    {
        if (TeleJellyPlugin.Instance?.Configuration.DownloadManager.Enabled != true)
        {
            return;
        }

        await CleanupExpiredDownloadsAsync(stoppingToken);

        var activeDownloads = _downloads.Values
            .Where(d => d.Status != DownloadStatus.Completed && d.Status != DownloadStatus.Failed && d.Status != DownloadStatus.Canceled)
            .ToArray();

        foreach (var download in activeDownloads)
        {
            if (!_processingDownloads.TryAdd(download.Id, 0))
            {
                continue;
            }

            try
            {
                await ProcessDownloadAsync(download, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process download {DownloadId}", download.Id);
                await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
            }
            finally
            {
                _processingDownloads.TryRemove(download.Id, out _);
            }
        }
    }

    private async Task RemoveFromBackendAsync(ManagedDownload download, bool deleteFiles, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(download.ServiceDownloadId) || string.IsNullOrWhiteSpace(download.ServiceName))
        {
            return;
        }

        try
        {
            if (download.ServiceType == DownloadServiceType.Torrent)
            {
                var torrentService = _torrentServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
                if (torrentService != null)
                {
                    await torrentService.RemoveDownloadAsync(download.ServiceDownloadId, deleteFiles, ct);
                }
            }
            else
            {
                var hostedService = _hostedServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
                if (hostedService != null)
                {
                    await hostedService.RemoveDownloadAsync(download.ServiceDownloadId, deleteFiles, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove backend download {ServiceDownloadId} from {ServiceName}",
                download.ServiceDownloadId,
                download.ServiceName);
        }
    }

    private void CleanupManagedFiles(ManagedDownload download)
    {
        foreach (var path in EnumerateLocalCleanupPaths(download))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete managed download path {Path}", path);
            }
        }
    }

    private IEnumerable<string> EnumerateLocalCleanupPaths(ManagedDownload download)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        if (config == null)
        {
            yield break;
        }

        var allowedRoots = new[]
        {
            config.TorrentServices.Transmission.StagingPath,
            config.TorrentServices.QBittorrent.StagingPath,
            config.HostedServices.JDownloader2.StagingPath,
            config.HostedServices.PyLoad.StagingPath
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var candidates = new[]
        {
            download.CurrentStagingPath,
            download.OriginalDownloadPath
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (allowedRoots.Any(root => candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            {
                yield return candidate;
            }
        }
    }

    private async Task CleanupExpiredDownloadsAsync(CancellationToken ct)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        if (config == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expiredDownloads = _downloads.Values
            .Where(download =>
            {
                if (!download.CompletedAt.HasValue)
                {
                    return false;
                }

                if (download.Status == DownloadStatus.Completed && config.AutoRemoveCompletedAfterDays)
                {
                    return download.CompletedAt.Value <= now.AddDays(-config.AutoRemoveCompletedDays);
                }

                if (download.Status is DownloadStatus.Failed or DownloadStatus.Canceled && config.AutoRemoveFailedAfterDays)
                {
                    return download.CompletedAt.Value <= now.AddDays(-config.AutoRemoveFailedDays);
                }

                return false;
            })
            .Select(download => download.Id)
            .ToArray();

        foreach (var id in expiredDownloads)
        {
            ct.ThrowIfCancellationRequested();
            if (_downloads.TryGetValue(id, out var expiredDownload))
            {
                _logger.LogInformation(
                    "Auto-removing expired download {DownloadId} ({Title}) with status {Status}",
                    expiredDownload.Id,
                    expiredDownload.Title,
                    expiredDownload.Status);
            }

            await RemoveDownloadAsync(id, false, ct);
        }
    }

    private static string? NormalizeExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        if (File.Exists(path))
        {
            return Path.GetDirectoryName(path);
        }

        return null;
    }

    private bool TryUpdateProgress(ManagedDownload download, double progressPercentage)
    {
        return DownloadWorkflowPolicies.TryUpdateProgress(download, progressPercentage, DateTime.UtcNow);
    }

    private bool HasDownloadTimedOut(ManagedDownload download, out string? reason)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        return DownloadWorkflowPolicies.HasDownloadTimedOut(download, config, DateTime.UtcNow, out reason);
    }

    private bool HasTorrentAvailabilityTimedOut(ManagedDownload download, object progress, out string? reason)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        return DownloadWorkflowPolicies.HasTorrentAvailabilityTimedOut(download, progress, config, DateTime.UtcNow, out reason);
    }

    private async Task<bool> FinalizeCompletedDownloadAsync(
        ManagedDownload download,
        Func<Task<string?>> getDownloadDirectoryAsync,
        Func<Task<FileInfo[]>> getCompletedFilesAsync)
    {
        var completedFiles = await getCompletedFilesAsync();
        var completedDirectory = completedFiles
            .Select(file => file.DirectoryName)
            .FirstOrDefault(directory => !string.IsNullOrWhiteSpace(directory));

        download.OriginalDownloadPath = completedDirectory ?? await getDownloadDirectoryAsync();
        download.OriginalDownloadPath = NormalizeExistingPath(download.OriginalDownloadPath) ?? download.OriginalDownloadPath;
        download.LastProgressAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(download.OriginalDownloadPath))
        {
            return false;
        }

        await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
        return true;
    }

    private async Task ProcessDownloadAsync(ManagedDownload download, CancellationToken stoppingToken)
    {
        switch (download.Status)
        {
            case DownloadStatus.Pending:
                if (CanStartAnotherDownload(download.Id))
                {
                    await InitiateDownloadAsync(download.Id, stoppingToken);
                }
                break;
            case DownloadStatus.Downloading:
                await CheckDownloadProgress(download, stoppingToken);
                break;
            case DownloadStatus.Extracting:
                await ExtractFiles(download, stoppingToken);
                break;
            case DownloadStatus.Analyzing:
                await AnalyzeFiles(download, stoppingToken);
                break;
            case DownloadStatus.Organizing:
                await OrganizeFiles(download, stoppingToken);
                break;
        }
    }

    private async Task CheckDownloadProgress(ManagedDownload download, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(download.ServiceDownloadId) || string.IsNullOrEmpty(download.ServiceName))
        {
            return;
        }

        var hasChanges = false;
        var existingPath = NormalizeExistingPath(download.OriginalDownloadPath);
        if (existingPath != null && download.OriginalDownloadPath != existingPath)
        {
            download.OriginalDownloadPath = existingPath;
            hasChanges = true;
        }

        if (download.ServiceType == DownloadServiceType.Torrent)
        {
            var service = _torrentServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
            if (service == null)
            {
                return;
            }

            var progress = await service.GetProgressAsync(download.ServiceDownloadId, ct);
            if (progress == null)
            {
                if (existingPath != null)
                {
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
                    return;
                }

                if (HasDownloadTimedOut(download, out var timeoutReason))
                {
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Stalled, timeoutReason);
                }

                return;
            }

            var progressType = progress.GetType();
            var percentProperty = progressType.GetProperty("PercentDone") ?? progressType.GetProperty("Progress");
            var currentProgress = download.ProgressPercentage;
            if (percentProperty != null)
            {
                var percentValue = Convert.ToDouble(percentProperty.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                currentProgress = percentValue <= 1.0 ? percentValue * 100 : percentValue;
                hasChanges |= TryUpdateProgress(download, currentProgress);
            }

            if (currentProgress >= 100 &&
                await FinalizeCompletedDownloadAsync(
                    download,
                    () => service.GetDownloadDirectoryAsync(download.ServiceDownloadId, ct),
                    () => service.GetCompletedFilesAsync(download.ServiceDownloadId, ct)))
            {
                return;
            }

            if (HasTorrentAvailabilityTimedOut(download, progress, out var noSeedsReason))
            {
                await UpdateDownloadStatus(download.Id, DownloadStatus.Stalled, noSeedsReason);
                return;
            }
        }
        else // Hosted
        {
            var service = _hostedServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
            if (service == null)
            {
                return;
            }

            var progress = await service.GetProgressAsync(download.ServiceDownloadId, ct);
            if (progress == null)
            {
                if (existingPath != null)
                {
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
                    return;
                }

                if (HasDownloadTimedOut(download, out var timeoutReason))
                {
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Stalled, timeoutReason);
                }

                return;
            }

            var progressType = progress.GetType();

            var bytesTotalProp = progressType.GetProperty("BytesTotal") ?? progressType.GetProperty("Size");
            var bytesLoadedProp = progressType.GetProperty("BytesLoaded");
            var linksDoneProp = progressType.GetProperty("LinksDone");
            var linksProp = progressType.GetProperty("Links");
            var currentProgress = download.ProgressPercentage;

            if (bytesTotalProp != null && bytesLoadedProp != null)
            {
                var bytesTotal = Convert.ToInt64(bytesTotalProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                var bytesLoaded = Convert.ToInt64(bytesLoadedProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                if (bytesTotal > 0)
                {
                    currentProgress = (double)bytesLoaded / bytesTotal * 100;
                    hasChanges |= TryUpdateProgress(download, currentProgress);
                }
            }
            else if (linksDoneProp != null && linksProp != null)
            {
                var links = Convert.ToInt32(linksProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                var linksDone = Convert.ToInt32(linksDoneProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                if (links > 0)
                {
                    currentProgress = (double)linksDone / links * 100;
                    hasChanges |= TryUpdateProgress(download, currentProgress);
                }
            }

            var statusProp = progressType.GetProperty("Status");
            var status = statusProp?.GetValue(progress) as string;
            if (string.Equals(status, "Finished", StringComparison.OrdinalIgnoreCase) &&
                await FinalizeCompletedDownloadAsync(
                    download,
                    () => service.GetDownloadDirectoryAsync(download.ServiceDownloadId, ct),
                    () => service.GetCompletedFilesAsync(download.ServiceDownloadId, ct)))
            {
                return;
            }
        }

        if (HasDownloadTimedOut(download, out var reason))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Stalled, reason);
            return;
        }

        if (hasChanges)
        {
            await SaveDownloadsAsync();
        }
    }

    private async Task ExtractFiles(ManagedDownload download, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(download.OriginalDownloadPath))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Original download path is missing.");
            return;
        }

        var normalizedSourcePath = NormalizeExistingPath(download.OriginalDownloadPath) ?? download.OriginalDownloadPath;
        if (string.IsNullOrWhiteSpace(normalizedSourcePath))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Download path no longer exists.");
            return;
        }

        download.OriginalDownloadPath = normalizedSourcePath;
        var archives = await _archiveExtractor.DetectArchivesAsync(normalizedSourcePath);
        if (archives.Any())
        {
            download.RequiresExtraction = true;
            _logger.LogInformation("Extracting {ArchiveCount} archive(s) for download {DownloadId} ({Title})", archives.Count(), download.Id, download.Title);
            var extractionPath = Path.Combine(normalizedSourcePath, "extracted");
            Directory.CreateDirectory(extractionPath);
            download.CurrentStagingPath = extractionPath;

            var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager.Extraction;
            var passwords = config.Passwords
                .Concat(string.IsNullOrWhiteSpace(download.SourcePassword) ? [] : [download.SourcePassword!])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            download.TriedPasswords = passwords;

            if (Directory.EnumerateFiles(extractionPath, "*", SearchOption.AllDirectories).Any())
            {
                await UpdateDownloadStatus(download.Id, DownloadStatus.Analyzing);
                return;
            }

            foreach (var archive in archives)
            {
                try
                {
                    await _archiveExtractor.ExtractArchiveAsync(archive.FullName, extractionPath, passwords, new Progress<int>(), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract archive {ArchiveName} for download {DownloadId}", archive.Name, download.Id);
                    var attemptedPasswordCount = download.TriedPasswords?.Length ?? 0;
                    var passwordInfo = attemptedPasswordCount > 0
                        ? $" after trying {attemptedPasswordCount} password candidate(s)"
                        : string.Empty;
                    await UpdateDownloadStatus(download.Id, DownloadStatus.ExtractionFailed, $"Failed to extract {archive.Name}{passwordInfo}.");
                    return;
                }
            }

            await UpdateDownloadStatus(download.Id, DownloadStatus.Analyzing);
        }
        else
        {
            download.RequiresExtraction = false;
            download.TriedPasswords = null;
            download.CurrentStagingPath = normalizedSourcePath;
            _logger.LogInformation("No archives detected for download {DownloadId} ({Title}); continuing to analysis", download.Id, download.Title);
            await UpdateDownloadStatus(download.Id, DownloadStatus.Analyzing);
        }
    }

    private async Task AnalyzeFiles(ManagedDownload download, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(download.CurrentStagingPath))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Staging path is missing.");
            return;
        }

        var fileGroups = await _mediaAnalyzer.AnalyzeAndGroupFilesAsync(download.CurrentStagingPath);
        download.AnalyzedFiles = fileGroups;
        _logger.LogInformation(
            "Analyzed {GroupCount} file group(s) for download {DownloadId} ({Title})",
            fileGroups.Length,
            download.Id,
            download.Title);

        var mainVideoFile = fileGroups.FirstOrDefault()?.VideoFile?.Path;
        var (season, episode) = await _mediaAnalyzer.ExtractSeasonAndEpisode(mainVideoFile ?? download.Title);
        download.Season = season;
        download.Episode = episode;

        await UpdateDownloadStatus(download.Id, DownloadStatus.Organizing);
    }

    private async Task OrganizeFiles(ManagedDownload download, CancellationToken ct)
    {
        if (download.AnalyzedFiles == null || !download.AnalyzedFiles.Any() || string.IsNullOrEmpty(download.TargetLibraryId))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "No files to organize or target library not set.");
            return;
        }

        var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager;
        var librarySettings = config.LibrarySettings.FirstOrDefault(l => l.LibraryId == download.TargetLibraryId) ?? new LibrarySettings();
        var library = _libraryManager.GetItemById(download.TargetLibraryId);
        if (library?.Path == null)
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Target library not found or path is missing.");
            return;
        }

        var mainVideoFile = download.AnalyzedFiles.FirstOrDefault()?.VideoFile?.Path;

        var finalPath = !string.IsNullOrWhiteSpace(download.UserConfirmedPath)
            ? await _pathTemplater.ResolvePathAsync(library.Path, download.UserConfirmedPath)
            : await _pathTemplater.ResolveTemplatePathAsync(
                library.Path,
                librarySettings.PathTemplate,
                download,
                download.FilledPathVariables ?? new Dictionary<string, string>(),
                mainVideoFile ?? download.Title);

        _logger.LogInformation(
            "Organizing download {DownloadId} ({Title}) into {Destination}",
            download.Id,
            download.Title,
            finalPath);
        await _fileOrganizer.MoveFilesToDestinationAsync(download.AnalyzedFiles, finalPath, new Progress<int>(), ct);
        if (config.TriggerLibraryScanAfterOrganize)
        {
            _fileOrganizer.TriggerLibraryScan(download.TargetLibraryId);
        }

        download.CompletedAt = DateTime.UtcNow;
        await UpdateDownloadStatus(download.Id, DownloadStatus.Completed);
    }

    public async Task<bool> InitiateDownloadAsync(Guid downloadId, CancellationToken ct)
    {
        if (TeleJellyPlugin.Instance?.Configuration.DownloadManager.Enabled != true)
        {
            await UpdateDownloadStatus(downloadId, DownloadStatus.Failed, "Download manager is disabled.");
            return false;
        }

        if (!_downloads.TryGetValue(downloadId, out var download))
        {
            _logger.LogError("Download {DownloadId} not found", downloadId);
            return false;
        }

        if (string.IsNullOrEmpty(download.LinkOrMagnet))
        {
            _logger.LogError("Download {DownloadId} has no link or magnet", downloadId);
            await UpdateDownloadStatus(downloadId, DownloadStatus.Failed, "No download link specified");
            return false;
        }

        if (!CanStartAnotherDownload(downloadId))
        {
            await UpdateDownloadStatus(downloadId, DownloadStatus.Pending, "Waiting for a free download slot.");
            return true;
        }

        var serviceType = IsHostedDownload(download.LinkOrMagnet)
            ? DownloadServiceType.Hosted
            : DownloadServiceType.Torrent;

        var candidates = SelectServicesForDownload(download.LinkOrMagnet, serviceType).ToArray();
        if (!candidates.Any())
        {
            _logger.LogError("No available services found for download {DownloadId}", downloadId);
            await UpdateDownloadStatus(downloadId, DownloadStatus.Failed, "No download service available");
            return false;
        }

        foreach (var candidate in candidates)
        {
            var serviceName = candidate switch
            {
                ITorrentDownloadService torrent => torrent.ServiceName,
                IHostedDownloadService hosted => hosted.ServiceName,
                _ => "unknown"
            };

            _logger.LogInformation(
                "Trying service {ServiceName} for download {DownloadId} ({Title})",
                serviceName,
                download.Id,
                download.Title);

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string serviceDownloadId;
                    if (candidate is ITorrentDownloadService torrentService)
                    {
                        serviceDownloadId = await torrentService.AddDownloadAsync(download.LinkOrMagnet, ct);
                        _logger.LogInformation("Started torrent download with {ServiceName}: {DownloadId}", torrentService.ServiceName, serviceDownloadId);
                    }
                    else if (candidate is IHostedDownloadService hostedService)
                    {
                        serviceDownloadId = await hostedService.AddDownloadAsync(download.LinkOrMagnet, ct);
                        _logger.LogInformation("Started hosted download with {ServiceName}: {DownloadId}", hostedService.ServiceName, serviceDownloadId);
                    }
                    else
                    {
                        break;
                    }

                    download.ServiceDownloadId = serviceDownloadId;
                    download.ServiceName = serviceName;
                    download.ServiceType = serviceType;
                    download.ProgressPercentage = 0;
                    download.LastProgressAt = DateTime.UtcNow;
                    download.StartAttempts++;
                    download.CompletedAt = null;
                    download.ErrorMessage = null;

                    await UpdateDownloadStatus(downloadId, DownloadStatus.Downloading);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Attempt {Attempt} failed for download {DownloadId} using service {ServiceName}",
                        attempt, downloadId, serviceName);

                    if (attempt >= 3)
                    {
                        break;
                    }

                    var backoffSeconds = Math.Pow(2, attempt - 1);
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
                }
            }
        }

        await UpdateDownloadStatus(downloadId, DownloadStatus.Failed, "Failed to start download on all available services.");
        return false;
    }

    private bool CanStartAnotherDownload(Guid currentDownloadId)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        if (config == null || config.MaxConcurrentDownloads <= 0)
        {
            return true;
        }

        var activeDownloadCount = _downloads.Values.Count(download =>
            download.Id != currentDownloadId &&
            download.Status == DownloadStatus.Downloading);

        return activeDownloadCount < config.MaxConcurrentDownloads;
    }

    private IEnumerable<object> SelectServicesForDownload(string linkOrMagnet, DownloadServiceType serviceType)
    {
        if (serviceType == DownloadServiceType.Torrent)
        {
            var availableServices = _healthMonitor.GetAvailableTorrentServices()
                .Where(s => s.IsEnabled && s.CanHandle(linkOrMagnet))
                .ToList();

            if (availableServices.Any())
            {
                foreach (var service in availableServices)
                {
                    _logger.LogInformation("Torrent service candidate: {ServiceName}", service.ServiceName);
                    yield return service;
                }

                yield break;
            }
        }
        else
        {
            var availableServices = _healthMonitor.GetAvailableHostedServices()
                .Where(s => s.IsEnabled && s.CanHandle(linkOrMagnet))
                .ToList();

            if (availableServices.Any())
            {
                foreach (var service in availableServices)
                {
                    _logger.LogInformation("Hosted service candidate: {ServiceName}", service.ServiceName);
                    yield return service;
                }

                yield break;
            }
        }

        _logger.LogWarning("No available {ServiceType} service found for link: {Link}", serviceType, linkOrMagnet);
    }

    private bool IsHostedDownload(string linkOrMagnet)
    {
        return !string.IsNullOrEmpty(linkOrMagnet) &&
               Uri.TryCreate(linkOrMagnet, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "http" || uri.Scheme == "https") &&
               !linkOrMagnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RestoreDownloadsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Restoring downloads from persistence...");
        try
        {
            if (File.Exists(_persistencePath))
            {
                var json = await File.ReadAllTextAsync(_persistencePath, ct);
                var restored = JsonSerializer.Deserialize<IEnumerable<ManagedDownload>>(json);
                var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
                var restoredCount = 0;

                foreach (var download in restored ?? [])
                {
                    if (config != null && download.CompletedAt.HasValue)
                    {
                        if (download.Status == DownloadStatus.Completed &&
                            config.AutoRemoveCompletedAfterDays &&
                            download.CompletedAt.Value <= DateTime.UtcNow.AddDays(-config.AutoRemoveCompletedDays))
                        {
                            continue;
                        }

                        if (download.Status is DownloadStatus.Failed or DownloadStatus.Canceled &&
                            config.AutoRemoveFailedAfterDays &&
                            download.CompletedAt.Value <= DateTime.UtcNow.AddDays(-config.AutoRemoveFailedDays))
                        {
                            continue;
                        }
                    }

                    download.LastStatusChangeAt = download.LastStatusChangeAt == default ? download.StartedAt : download.LastStatusChangeAt;
                    _downloads.TryAdd(download.Id, download);
                    restoredCount++;
                }

                _logger.LogInformation("Restored {Count} persisted downloads.", restoredCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore downloads from persistence.");
        }
    }

    private async Task SaveDownloadsAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_downloads.Values, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_persistencePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save downloads to persistence.");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task WithDownloadLockAsync(Guid id, Func<Task> action)
    {
        var lockForDownload = _downloadLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await lockForDownload.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            lockForDownload.Release();
        }
    }

    private static bool IsValidTransition(DownloadStatus from, DownloadStatus to)
    {
        return DownloadWorkflowPolicies.IsValidTransition(from, to);
    }
}
