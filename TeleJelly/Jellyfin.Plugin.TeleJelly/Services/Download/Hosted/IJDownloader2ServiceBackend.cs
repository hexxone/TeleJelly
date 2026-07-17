using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal interface IJDownloader2ServiceBackend : IDisposable
{
    Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct);
    Task<string> AddContainerAsync(byte[] content, string containerType, CancellationToken ct);
    Task<JDownloaderContainerImportProgress> GetContainerImportProgressAsync(string crawlerJobId, CancellationToken ct);
    Task<string> CompleteContainerImportAsync(string crawlerJobId, CancellationToken ct);
    Task CancelContainerImportAsync(string crawlerJobId, CancellationToken ct);
    Task<object?> GetProgressAsync(string downloadId, CancellationToken ct);
    Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct);
    Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct);
    Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);
}
