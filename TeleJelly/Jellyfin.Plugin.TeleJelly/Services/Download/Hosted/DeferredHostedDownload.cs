using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal interface IDeferredHostedDownloadService
{
    bool IsDeferredDownload(string downloadId);
    Task<DeferredHostedDownloadProgress> ContinueDownloadAsync(string downloadId, CancellationToken ct);
}

internal sealed record DeferredHostedDownloadProgress(
    bool IsComplete,
    bool IsFailed,
    string StatusText,
    string? ResolvedDownloadId = null);

internal sealed record JDownloaderContainerImportProgress(
    bool IsComplete,
    bool IsFailed,
    int Crawled,
    int Broken,
    int Filtered,
    string StatusText);

internal sealed class JDownloaderAggregateProgress
{
    public long BytesLoaded { get; set; }
    public long BytesTotal { get; set; }
    public int Links { get; set; }
    public int LinksDone { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SaveTo { get; set; }
}
