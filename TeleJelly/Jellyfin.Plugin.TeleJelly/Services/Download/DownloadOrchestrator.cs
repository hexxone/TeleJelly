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
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

public interface IDownloadOrchestrator
{
    Task<ManagedDownload> BeginDownloadWorkflow(string imdbId, long chatId, long userId, string? link = null);
    Task UpdateDownloadStatus(Guid id, DownloadStatus newStatus, string? errorMessage = null);
    ManagedDownload? GetDownload(Guid id);
    IEnumerable<ManagedDownload> GetAllDownloads();
    Task ProcessAllDownloadsAsync(CancellationToken stoppingToken);
    Task<bool> InitiateDownloadAsync(Guid downloadId, CancellationToken ct);
    Task RestoreDownloadsAsync(CancellationToken ct);
}

/// <summary>
///     TODO make this 100% thread and "kill" safe.
///     TODO The process should be able to crash at any time externally and should still be able to recover.
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
        _configurationManager = configurationManager;
        _healthMonitor = healthMonitor;
        _persistencePath = Path.Combine(_configurationManager.ApplicationPaths.DataPath, "TeleJelly_Downloads.json");
    }

    public async Task<ManagedDownload> BeginDownloadWorkflow(string imdbId, long chatId, long userId, string? link = null)
    {
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
            StartedAt = DateTime.UtcNow
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

            download.Status = newStatus;
            download.ErrorMessage = errorMessage;
            await SaveDownloadsAsync();
        });
    }

    public ManagedDownload? GetDownload(Guid id)
    {
        return _downloads.GetValueOrDefault(id);
    }

    public IEnumerable<ManagedDownload> GetAllDownloads()
    {
        return _downloads.Values;
    }

    public async Task ProcessAllDownloadsAsync(CancellationToken stoppingToken)
    {
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

    private async Task ProcessDownloadAsync(ManagedDownload download, CancellationToken stoppingToken)
    {
        switch (download.Status)
        {
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
                return;
            }

            // Use reflection to handle different progress object types (Transmission, qBittorrent, etc.)
            var progressType = progress.GetType();

            // Try to get progress percentage (property names: PercentDone, Progress)
            var percentProperty = progressType.GetProperty("PercentDone") ?? progressType.GetProperty("Progress");
            if (percentProperty != null)
            {
                var percentValue = Convert.ToDouble(percentProperty.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                download.ProgressPercentage = percentValue * 100;
            }

            // Try to get download directory (property names: DownloadDir, SavePath)
            var dirProperty = progressType.GetProperty("DownloadDir") ?? progressType.GetProperty("SavePath");
            if (dirProperty != null)
            {
                var dirValue = dirProperty.GetValue(progress) as string;

                // Check if download is complete
                if (percentProperty != null)
                {
                    var percentValue = Convert.ToDouble(percentProperty.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                    if (percentValue >= 1.0 && !string.IsNullOrEmpty(dirValue))
                    {
                        download.OriginalDownloadPath = dirValue;
                        await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
                    }
                }
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
                return;
            }

            var progressType = progress.GetType();

            // Calculate progress percentage from bytes
            var bytesTotalProp = progressType.GetProperty("BytesTotal") ?? progressType.GetProperty("Size");
            var bytesLoadedProp = progressType.GetProperty("BytesLoaded");
            var linksDoneProp = progressType.GetProperty("LinksDone");
            var linksProp = progressType.GetProperty("Links");

            if (bytesTotalProp != null && bytesLoadedProp != null)
            {
                // JDownloader2 style: BytesTotal / BytesLoaded
                var bytesTotal = Convert.ToInt64(bytesTotalProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                var bytesLoaded = Convert.ToInt64(bytesLoadedProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                if (bytesTotal > 0)
                {
                    download.ProgressPercentage = (double)bytesLoaded / bytesTotal * 100;
                }
            }
            else if (linksDoneProp != null && linksProp != null)
            {
                // PyLoad style: Links / LinksDone
                var links = Convert.ToInt32(linksProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                var linksDone = Convert.ToInt32(linksDoneProp.GetValue(progress), System.Globalization.CultureInfo.InvariantCulture);
                if (links > 0)
                {
                    download.ProgressPercentage = (double)linksDone / links * 100;
                }
            }

            // Check if download is finished
            var statusProp = progressType.GetProperty("Status");
            var folderProp = progressType.GetProperty("SaveTo") ?? progressType.GetProperty("Folder");

            if (statusProp != null && folderProp != null)
            {
                var status = statusProp.GetValue(progress) as string;
                var folder = folderProp.GetValue(progress) as string;

                if (status == "Finished" && !string.IsNullOrEmpty(folder))
                {
                    download.OriginalDownloadPath = folder;
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
                }
            }
        }
    }

    private async Task ExtractFiles(ManagedDownload download, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(download.OriginalDownloadPath))
        {
            await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Original download path is missing.");
            return;
        }

        var archives = await _archiveExtractor.DetectArchivesAsync(download.OriginalDownloadPath);
        if (archives.Any())
        {
            download.RequiresExtraction = true;
            var extractionPath = Path.Combine(download.OriginalDownloadPath, "extracted");
            Directory.CreateDirectory(extractionPath);
            download.CurrentStagingPath = extractionPath;

            var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager.Extraction;
            var passwords = config.Passwords
                .Concat(string.IsNullOrWhiteSpace(download.SourcePassword) ? [] : [download.SourcePassword!])
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var archive in archives)
            {
                try
                {
                    await _archiveExtractor.ExtractArchiveAsync(archive.FullName, extractionPath, passwords, new Progress<int>(), ct);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    await UpdateDownloadStatus(download.Id, DownloadStatus.ExtractionFailed, $"Failed to extract {archive.Name}.");
                    return;
                }
            }

            await UpdateDownloadStatus(download.Id, DownloadStatus.Analyzing);
        }
        else
        {
            download.CurrentStagingPath = download.OriginalDownloadPath;
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
        var mainVideoFile = download.AnalyzedFiles.FirstOrDefault()?.VideoFile?.Path;

        var finalPath = await _pathTemplater.ApplyTemplateAsync(
            librarySettings.PathTemplate,
            download,
            download.FilledPathVariables ?? new Dictionary<string, string>(),
            mainVideoFile ?? download.Title);

        await _fileOrganizer.MoveFilesToDestinationAsync(download.AnalyzedFiles, finalPath, new Progress<int>(), ct);
        _fileOrganizer.TriggerLibraryScan(download.TargetLibraryId);

        download.CompletedAt = DateTime.UtcNow;
        await UpdateDownloadStatus(download.Id, DownloadStatus.Completed);
    }

    public async Task<bool> InitiateDownloadAsync(Guid downloadId, CancellationToken ct)
    {
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

                foreach (var download in restored!)
                {
                    // Don't restore completed/failed downloads from a week ago
                    if ((download.Status == DownloadStatus.Completed || download.Status == DownloadStatus.Failed) &&
                        download.CompletedAt.HasValue && (DateTime.UtcNow - download.CompletedAt.Value).TotalDays > 7)
                    {
                        continue;
                    }

                    // Reset transient states
                    if (download.Status == DownloadStatus.Downloading || download.Status == DownloadStatus.Extracting)
                    {
                        download.Status = DownloadStatus.Stalled;
                    }

                    _downloads.TryAdd(download.Id, download);
                }

                _logger.LogInformation("Restored {Count} downloads.", _downloads.Count);
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
        if (from == to)
        {
            return true;
        }

        if (to is DownloadStatus.Failed or DownloadStatus.Canceled)
        {
            return true;
        }

        return from switch
        {
            DownloadStatus.Pending => to is DownloadStatus.AwaitingLibrary or DownloadStatus.AwaitingMediaType,
            DownloadStatus.AwaitingLibrary => to == DownloadStatus.AwaitingMediaType,
            DownloadStatus.AwaitingMediaType => to is DownloadStatus.AwaitingSeason or DownloadStatus.AwaitingSearchResult or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingSeason => to is DownloadStatus.AwaitingSearchResult or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingSearchResult => to is DownloadStatus.AwaitingPathVars or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingPathVars => to is DownloadStatus.AwaitingPathVars or DownloadStatus.AwaitingPathConfirm,
            DownloadStatus.AwaitingPathConfirm => to == DownloadStatus.Downloading,
            DownloadStatus.Downloading => to is DownloadStatus.Extracting or DownloadStatus.Stalled,
            DownloadStatus.Extracting => to is DownloadStatus.Analyzing or DownloadStatus.ExtractionFailed,
            DownloadStatus.ExtractionFailed => to == DownloadStatus.Extracting,
            DownloadStatus.Analyzing => to == DownloadStatus.Organizing,
            DownloadStatus.Organizing => to == DownloadStatus.Completed,
            DownloadStatus.Stalled => to is DownloadStatus.Downloading or DownloadStatus.Extracting,
            _ => false
        };
    }
}
