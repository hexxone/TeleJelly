using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

/// <summary>
///     TODO actually use Interface... currently only "GetProgressAsync" is used.
/// </summary>
public interface IHostedDownloadService
{
    string ServiceName { get; }
    bool IsEnabled { get; }

    bool CanHandle(string linkOrFile);
    Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct);
    Task<object?> GetProgressAsync(string downloadId, CancellationToken ct);
    // TODO unused ??
    Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct);
    // TODO unused ??
    Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct);
    // TODO unused ??
    Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);
    // TODO unused ??
    Task<string?> ExtractPasswordFromDlcAsync(byte[] dlcContent, CancellationToken ct);
}
