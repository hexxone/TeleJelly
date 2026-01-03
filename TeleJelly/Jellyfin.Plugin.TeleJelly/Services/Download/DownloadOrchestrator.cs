using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JDownloader.Model;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download
{
    /// <summary>
    ///     TODO make this 100% thread and "kill" safe.
    ///     TODO The process should be able to crash at any time externally and should still be able to recover.
    /// </summary>
    public class DownloadOrchestrator
    {
        private readonly ILogger<DownloadOrchestrator> _logger;
        private readonly IEnumerable<ITorrentDownloadService> _torrentServices;
        private readonly IEnumerable<IHostedDownloadService> _hostedServices;
        private readonly ArchiveExtractionService _archiveExtractor;
        private readonly MediaAnalyzerService _mediaAnalyzer;
        private readonly PathTemplateService _pathTemplater;
        private readonly MediaFileOrganizerService _fileOrganizer;
        private readonly IServerConfigurationManager _configurationManager;

        private readonly ConcurrentDictionary<Guid, ManagedDownload> _downloads = new();
        private readonly string _persistencePath;

        public DownloadOrchestrator(
            ILogger<DownloadOrchestrator> logger,
            IEnumerable<ITorrentDownloadService> torrentServices,
            IEnumerable<IHostedDownloadService> hostedServices,
            ArchiveExtractionService archiveExtractor,
            MediaAnalyzerService mediaAnalyzer,
            PathTemplateService pathTemplater,
            MediaFileOrganizerService fileOrganizer,
            IServerConfigurationManager configurationManager)
        {
            _logger = logger;
            _torrentServices = torrentServices;
            _hostedServices = hostedServices;
            _archiveExtractor = archiveExtractor;
            _mediaAnalyzer = mediaAnalyzer;
            _pathTemplater = pathTemplater;
            _fileOrganizer = fileOrganizer;
            _configurationManager = configurationManager;
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
            if (_downloads.TryGetValue(id, out var download))
            {
                download.Status = newStatus;
                download.ErrorMessage = errorMessage;
                await SaveDownloadsAsync();
            }
        }

        public ManagedDownload? GetDownload(Guid id) => _downloads.GetValueOrDefault(id);
        public IEnumerable<ManagedDownload> GetAllDownloads() => _downloads.Values;

        public async Task ProcessAllDownloadsAsync(CancellationToken stoppingToken)
        {
            foreach (var download in _downloads.Values.Where(d => d.Status != DownloadStatus.Completed && d.Status != DownloadStatus.Failed))
            {
                try
                {
                    await ProcessDownloadAsync(download, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process download {DownloadId}", download.Id);
                    await UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
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
            if (string.IsNullOrEmpty(download.ServiceDownloadId) || string.IsNullOrEmpty(download.ServiceName)) return;

            if (download.ServiceType == DownloadServiceType.Torrent)
            {
                var service = _torrentServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
                if (service != null && await service.GetProgressAsync(download.ServiceDownloadId, ct) is Transmission.API.RPC.Entity.TorrentInfo progress)
                {
                    download.ProgressPercentage = progress.PercentDone * 100;
                    if (progress.PercentDone >= 1 && !string.IsNullOrEmpty(progress.DownloadDir))
                    {
                        download.OriginalDownloadPath = progress.DownloadDir;
                        await UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
                    }
                }
            }
            else // Hosted
            {
                var service = _hostedServices.FirstOrDefault(s => s.ServiceName == download.ServiceName);
                if (service != null && await service.GetProgressAsync(download.ServiceDownloadId, ct) is FilePackage progress)
                {
                    if (progress.BytesTotal > 0)
                        download.ProgressPercentage = (double)progress.BytesLoaded / progress.BytesTotal * 100;

                    if (progress.Status == "Finished" && !string.IsNullOrEmpty(progress.SaveTo))
                    {
                        download.OriginalDownloadPath = progress.SaveTo;
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
                var passwords = config.Passwords.ToArray();

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
            var librarySettings = config.LibrarySettings.GetValueOrDefault(download.TargetLibraryId) ?? new LibrarySettings();
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
            try
            {
                var json = JsonSerializer.Serialize(_downloads.Values, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_persistencePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save downloads to persistence.");
            }
        }
    }
}
