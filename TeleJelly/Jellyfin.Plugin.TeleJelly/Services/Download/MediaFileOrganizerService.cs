using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

public class MediaFileOrganizerService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MediaFileOrganizerService> _logger;

    public MediaFileOrganizerService(ILogger<MediaFileOrganizerService> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    public async Task MoveFilesToDestinationAsync(MediaFileGroup[] groups, string destinationDirectory, IProgress<int>? progress, CancellationToken ct)
    {
        _logger.LogInformation("Organizing {NumGroups} file groups to destination: {Destination}", groups.Length, destinationDirectory);
        progress ??= new Progress<int>();

        await EnsureDirectoryExistsAsync(destinationDirectory);

        var totalFiles = groups.Sum(g => (g.VideoFile != null ? 1 : 0) + g.SubtitleFiles.Count);
        var movedFiles = 0;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            if (group.VideoFile != null)
            {
                await MoveFileWithConflictHandling(group.VideoFile.Path, destinationDirectory, ct);
                movedFiles++;
                progress.Report(movedFiles * 100 / totalFiles);
            }

            foreach (var subtitleFile in group.SubtitleFiles)
            {
                ct.ThrowIfCancellationRequested();
                await MoveFileWithConflictHandling(subtitleFile.Path, destinationDirectory, ct);
                movedFiles++;
                progress.Report(movedFiles * 100 / totalFiles);
            }
        }

        _logger.LogInformation("File organization complete.");
    }

    private async Task MoveFileWithConflictHandling(string sourcePath, string destinationDir, CancellationToken ct)
    {
        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(destinationDir, fileName);
        var finalDestinationPath = destinationPath;
        var counter = 1;

        while (File.Exists(finalDestinationPath))
        {
            _logger.LogWarning("File conflict detected at: {Path}. Attempting to resolve.", finalDestinationPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var newFileName = $"{fileNameWithoutExt}.{counter}{extension}";
            finalDestinationPath = Path.Combine(destinationDir, newFileName);
            counter++;
        }

        if (finalDestinationPath != destinationPath)
        {
            _logger.LogInformation("Conflict resolved. New path: {Path}", finalDestinationPath);
        }

        await Task.Run(() => File.Move(sourcePath, finalDestinationPath), ct);
        _logger.LogDebug("Successfully moved '{Source}' to '{Destination}'", sourcePath, finalDestinationPath);
    }

    private Task EnsureDirectoryExistsAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogInformation("Creating directory: {Path}", path);
            Directory.CreateDirectory(path);
        }

        return Task.CompletedTask;
    }

    public void TriggerLibraryScan(string libraryId)
    {
        _logger.LogInformation("Triggering Jellyfin library scan for library {LibraryId}", libraryId);
        var library = _libraryManager.GetLi;
        if (library != null)
        {
            _libraryManager.QueueLibraryScan();
        }
        else
        {
            _logger.LogWarning("Could not find library with ID {LibraryId} to trigger scan.", libraryId);
        }
    }
}
