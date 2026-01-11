using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

/// <summary>
///     TODO the Extractor should check for free disk space in the destination directory before extraction.
///     .. the margin should be 100% + 20% of the archive size.
///     .. if there is not enough space we need to to retry after some large time like 1h multiple times and inform the user if it doesnt get freed up.
///     TODO IMPORTANT! this space-check also needs to take "multi-part" archives into account.
///     TODO report accurate percentage progress during extraction to the User.
///     TODO add Option/Setting to delete extracted archives after successful extraction.
///     TODO what about ".tar.gz" and ".tar.bz2" files which are essentially double archives? Or wrapped archives like one zip in another?
///     .. maybe we should make this into a setting like "ExtractRecursiveDepth=0" for none (only top level), =1 for one etc. to avoid Zip-bombs..
/// </summary>
public class ArchiveExtractionService
{
    // TODO better way to determine if a file is an archive?
    private static readonly string[] ArchiveExtensions = [".rar", ".zip", ".7z", ".tar", ".gz", ".bz2"];
    private readonly ILogger<ArchiveExtractionService> _logger;

    public ArchiveExtractionService(ILogger<ArchiveExtractionService> logger)
    {
        _logger = logger;
    }

    public Task<FileInfo[]> DetectArchivesAsync(string directory)
    {
        _logger.LogInformation("Detecting archives in directory: {Directory}", directory);
        var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
        var archiveFiles = new List<FileInfo>();

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (ArchiveExtensions.Contains(fileInfo.Extension.ToLowerInvariant()) || IsMultiPartArchive(fileInfo.Name))
            {
                // For multi-part, only add the first file
                if (IsMultiPartArchive(fileInfo.Name) && !IsFirstMultiPart(fileInfo.Name))
                {
                    continue;
                }

                archiveFiles.Add(fileInfo);
            }
        }

        return Task.FromResult(archiveFiles.ToArray());
    }

    private bool IsMultiPartArchive(string fileName)
    {
        // TODO better way to determine if a file is an multi-part archive?
        return fileName.Contains(".part") || fileName.EndsWith(".001");
    }

    private bool IsFirstMultiPart(string fileName)
    {
        // TODO better way to determine if a file is an multi-part archive?
        return fileName.EndsWith(".part1.rar") || fileName.EndsWith(".part01.rar") || fileName.EndsWith(".001");
    }

    public async Task ExtractArchiveAsync(string archivePath, string destinationPath, string[] passwords, IProgress<int> progress, CancellationToken ct)
    {
        _logger.LogInformation("Starting extraction for archive: {ArchivePath}", archivePath);
        var successfulPassword = await TryAllPasswordsAsync(archivePath, passwords, ct);
        if (successfulPassword == null)
        {
            throw new Exception("Failed to extract archive. All provided passwords failed.");
        }

        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { Password = successfulPassword });
            var totalSize = archive.TotalSize;
            long extractedSize = 0;

            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                ct.ThrowIfCancellationRequested();
                entry.WriteToDirectory(destinationPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });

                extractedSize += entry.Size;
                var percentage = (int)((double)extractedSize / totalSize * 100);
                progress?.Report(percentage);
            }
        }, ct);

        _logger.LogInformation("Successfully extracted archive: {ArchivePath}", archivePath);
    }

    public Task<string> TryAllPasswordsAsync(string archivePath, string[] passwords, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            foreach (var password in passwords.Concat([null])) // Try with no password last
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    _logger.LogDebug("Trying password '{Password}' for archive {ArchivePath}", string.IsNullOrEmpty(password) ? "(none)" : "******", archivePath);
                    using (var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password }))
                    {
                        // Try to read the first entry to validate password
                        // TODO try to read the smallest file of the Archive to be most efficient?
                        if (archive.Entries.Any())
                        {
                            using var entryStream = archive.Entries.First().OpenEntryStream();
                            entryStream.ReadByte();
                        }
                    }

                    _logger.LogInformation("Found successful password for archive {ArchivePath}", archivePath);
                    return password;
                }
                catch (CryptographicException)
                {
                    _logger.LogDebug("Password failed for archive {ArchivePath}", archivePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to test password for archive {ArchivePath}", archivePath);
                }
            }

            return null;
        }, ct);
    }

    public Task<string[]> GetArchiveContentsAsync(string archivePath, string password = null)
    {
        return Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password });
            return archive.Entries.Where(e => !e.IsDirectory).Select(e => e.Key).ToArray();
        });
    }
}
