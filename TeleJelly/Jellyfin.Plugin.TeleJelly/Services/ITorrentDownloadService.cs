using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public interface ITorrentDownloadService
    {
        string ServiceName { get; }
        DownloadServiceType ServiceType => DownloadServiceType.Torrent;
        bool IsEnabled { get; }

        bool CanHandle(string linkOrMagnet);
        Task<string> AddDownloadAsync(string linkOrMagnet, CancellationToken ct);
        Task<object> GetProgressAsync(string downloadId, CancellationToken ct);
        Task<string> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct);
        Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct);
        Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct);
        Task<bool> TestConnectionAsync(CancellationToken ct);
    }
}
