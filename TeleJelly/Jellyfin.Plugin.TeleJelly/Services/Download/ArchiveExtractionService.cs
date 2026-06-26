using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

/// <summary>
/// Extracts archives from staged downloads, including password iteration, multipart archive-set detection,
/// bounded recursive extraction and destination free-space safety checks.
/// </summary>
internal sealed class ArchiveExtractionService
{
    private static readonly string[] ArchiveSuffixes =
    [
        ".tar.gz",
        ".tar.bz2",
        ".tgz",
        ".tbz2",
        ".rar",
        ".zip",
        ".7z",
        ".tar",
        ".gz",
        ".bz2"
    ];

    private static readonly Regex MultipartRarPattern = new(@"^(?<base>.+)\.part(?<part>\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultipartNumericPattern = new(@"^(?<base>.+)\.(?<part>\d{3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultipartRxxPattern = new(@"^(?<base>.+)\.r(?<part>\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<ArchiveExtractionService> _logger;

    public ArchiveExtractionService(ILogger<ArchiveExtractionService> logger)
    {
        _logger = logger;
    }

    public Task<FileInfo[]> DetectArchivesAsync(string path)
    {
        _logger.LogInformation("Detecting archives in path: {Path}", path);

        var archives = EnumerateCandidateFiles(path)
            .Select(candidate => new FileInfo(candidate))
            .Select(candidate => TryGetArchiveDescriptor(candidate, out var descriptor) ? descriptor : null)
            .Where(descriptor => descriptor != null)
            .GroupBy(descriptor => descriptor!.SetKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(entry => entry!.IsFirstPart) ?? group.First()!)
            .Select(entry => entry!.File)
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(archives);
    }

    public async Task ExtractArchiveAsync(string archivePath, string destinationPath, string[] passwords, IProgress<int> progress, CancellationToken ct)
    {
        _logger.LogInformation("Starting extraction for archive: {ArchivePath}", archivePath);
        var successfulPassword = await TryAllPasswordsAsync(archivePath, passwords, ct);
        if (successfulPassword == null)
        {
            throw new Exception("Failed to extract archive. All provided passwords failed.");
        }

        Directory.CreateDirectory(destinationPath);
        await EnsureSufficientFreeSpaceAsync(new FileInfo(archivePath), destinationPath, ct);
        await ExtractArchiveInternalAsync(archivePath, destinationPath, successfulPassword, progress, ct);

        var extractionConfig = TeleJellyPlugin.Instance?.Configuration.DownloadManager.Extraction;
        if (extractionConfig?.DeleteArchivesAfterExtraction == true)
        {
            DeleteArchiveSet(new FileInfo(archivePath));
        }

        var recursiveDepth = Math.Max(0, extractionConfig?.RecursiveExtractionDepth ?? 0);
        if (recursiveDepth > 0)
        {
            await ExtractNestedArchivesAsync(destinationPath, passwords, recursiveDepth, ct);
        }

        _logger.LogInformation("Successfully extracted archive: {ArchivePath}", archivePath);
    }

    public Task<string?> TryAllPasswordsAsync(string archivePath, string[] passwords, CancellationToken ct)
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
                        var smallestEntry = archive.Entries
                            .Where(entry => !entry.IsDirectory)
                            .OrderBy(entry => entry.Size)
                            .FirstOrDefault();

                        if (smallestEntry != null)
                        {
                            using var entryStream = smallestEntry.OpenEntryStream();
                            entryStream.ReadByte();
                        }
                    }

                    _logger.LogInformation("Found successful password for archive {ArchivePath}", archivePath);
                    return password;
                }
                catch (System.Security.Cryptography.CryptographicException)
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

    public Task<string[]> GetArchiveContentsAsync(string archivePath, string? password = null)
    {
        return Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password });

            return archive.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => e.Key)
                .Where(k => k != null)
                .Cast<string>()
                .ToArray();
        });
    }

    private async Task ExtractArchiveInternalAsync(string archivePath, string destinationPath, string? password, IProgress<int>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password });
            var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToArray();
            var totalSize = Math.Max(1L, entries.Sum(entry => Math.Max(1L, entry.Size)));
            long extractedSize = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                entry.WriteToDirectory(destinationPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });

                extractedSize += Math.Max(1L, entry.Size);
                progress?.Report((int)Math.Min(100, extractedSize * 100 / totalSize));
            }
        }, ct);
    }

    private async Task ExtractNestedArchivesAsync(string directoryPath, string[] passwords, int remainingDepth, CancellationToken ct)
    {
        if (remainingDepth <= 0)
        {
            return;
        }

        var nestedArchives = await DetectArchivesAsync(directoryPath);
        foreach (var nestedArchive in nestedArchives)
        {
            ct.ThrowIfCancellationRequested();

            var nestedDestination = Path.Combine(
                nestedArchive.DirectoryName ?? directoryPath,
                Path.GetFileNameWithoutExtension(nestedArchive.Name));

            Directory.CreateDirectory(nestedDestination);

            var successfulPassword = await TryAllPasswordsAsync(nestedArchive.FullName, passwords, ct);
            if (successfulPassword == null)
            {
                throw new Exception($"Failed to extract nested archive '{nestedArchive.Name}'. All provided passwords failed.");
            }

            await EnsureSufficientFreeSpaceAsync(nestedArchive, nestedDestination, ct);
            await ExtractArchiveInternalAsync(nestedArchive.FullName, nestedDestination, successfulPassword, null, ct);

            var extractionConfig = TeleJellyPlugin.Instance?.Configuration.DownloadManager.Extraction;
            if (extractionConfig?.DeleteArchivesAfterExtraction == true)
            {
                DeleteArchiveSet(nestedArchive);
            }

            await ExtractNestedArchivesAsync(nestedDestination, passwords, remainingDepth - 1, ct);
        }
    }

    private async Task EnsureSufficientFreeSpaceAsync(FileInfo archiveFile, string destinationPath, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        var extractionConfig = TeleJellyPlugin.Instance?.Configuration.DownloadManager.Extraction;
        var marginPercent = Math.Max(0, extractionConfig?.FreeSpaceMarginPercent ?? 20);
        var archiveSetSize = GetArchiveSetMembers(archiveFile)
            .Where(file => file.Exists)
            .Sum(file => file.Length);

        var requiredFreeSpace = archiveSetSize + (archiveSetSize * marginPercent / 100);
        var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return;
        }

        var drive = new DriveInfo(destinationRoot);
        if (drive.AvailableFreeSpace < requiredFreeSpace)
        {
            throw new IOException(
                $"Not enough free disk space to extract '{archiveFile.Name}'. Required {requiredFreeSpace} bytes, available {drive.AvailableFreeSpace} bytes.");
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string path)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static bool TryGetArchiveDescriptor(FileInfo file, out ArchiveDescriptor descriptor)
    {
        var fileName = file.Name.ToLowerInvariant();
        var directory = file.DirectoryName ?? string.Empty;

        var partMatch = MultipartRarPattern.Match(fileName);
        if (partMatch.Success && int.TryParse(partMatch.Groups["part"].Value, out var partNumber))
        {
            descriptor = new ArchiveDescriptor(file, Path.Combine(directory, partMatch.Groups["base"].Value), true, partNumber == 1);
            return true;
        }

        var numericMatch = MultipartNumericPattern.Match(fileName);
        if (numericMatch.Success && int.TryParse(numericMatch.Groups["part"].Value, out var numericPart))
        {
            descriptor = new ArchiveDescriptor(file, Path.Combine(directory, numericMatch.Groups["base"].Value), true, numericPart == 1);
            return true;
        }

        var rxxMatch = MultipartRxxPattern.Match(fileName);
        if (rxxMatch.Success && int.TryParse(rxxMatch.Groups["part"].Value, out var rxxPart))
        {
            descriptor = new ArchiveDescriptor(file, Path.Combine(directory, rxxMatch.Groups["base"].Value), true, rxxPart == 0);
            return true;
        }

        if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            var hasRxxSiblings = Directory.Exists(directory) &&
                                 Directory.EnumerateFiles(directory, $"{baseName}.r??", SearchOption.TopDirectoryOnly).Any();
            if (hasRxxSiblings)
            {
                descriptor = new ArchiveDescriptor(file, Path.Combine(directory, baseName.ToLowerInvariant()), true, true);
                return true;
            }
        }

        if (ArchiveSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            descriptor = new ArchiveDescriptor(file, file.FullName, false, true);
            return true;
        }

        descriptor = null!;
        return false;
    }

    private IEnumerable<FileInfo> GetArchiveSetMembers(FileInfo archiveFile)
    {
        if (!TryGetArchiveDescriptor(archiveFile, out var descriptor))
        {
            return [archiveFile];
        }

        if (!descriptor.IsMultipart)
        {
            return [archiveFile];
        }

        var directory = archiveFile.DirectoryName;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [archiveFile];
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(file => new FileInfo(file))
            .Where(candidate => TryGetArchiveDescriptor(candidate, out var candidateDescriptor) &&
                                candidateDescriptor.SetKey.Equals(descriptor.SetKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void DeleteArchiveSet(FileInfo archiveFile)
    {
        foreach (var file in GetArchiveSetMembers(archiveFile))
        {
            try
            {
                if (file.Exists)
                {
                    file.Delete();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete extracted archive part {ArchiveFile}", file.FullName);
            }
        }
    }

    private sealed record ArchiveDescriptor(FileInfo File, string SetKey, bool IsMultipart, bool IsFirstPart);
}
