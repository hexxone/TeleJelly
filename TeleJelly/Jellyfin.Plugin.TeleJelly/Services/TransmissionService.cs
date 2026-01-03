using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;
using Transmission.API.RPC;
using Transmission.API.RPC.Entity;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public class TransmissionService : ITorrentDownloadService
    {
        private readonly ILogger _logger;
        private readonly TransmissionSettings _config;

        public TransmissionService(ILogger<TransmissionService> logger, PluginConfiguration config)
        {
            _logger = logger;
            _config = config.DownloadManager.TorrentServices.Transmission;
        }

        public string ServiceName => "Transmission";
        public bool IsEnabled => _config.Enabled;

        private Client GetClient()
        {
            var url = $"http://{_config.Host}:{_config.Port}/transmission/rpc";
            return new Client(url, null, _config.Username, _config.Password);
        }

        public bool CanHandle(string linkOrMagnet)
        {
            return !string.IsNullOrEmpty(linkOrMagnet) &&
                   (linkOrMagnet.StartsWith("magnet:?xt=", StringComparison.OrdinalIgnoreCase) ||
                    (Uri.TryCreate(linkOrMagnet, UriKind.Absolute, out var uri) &&
                     uri.AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<string> AddDownloadAsync(string linkOrMagnet, CancellationToken ct)
        {
            var client = GetClient();
            var newTorrent = new NewTorrent { Filename = linkOrMagnet, Paused = false };
            var newTorrentInfo = await client.TorrentAddAsync(newTorrent, ct);

            if (newTorrentInfo?.ID == null)
            {
                _logger.LogError("Failed to add torrent to Transmission or retrieve its ID.");
                throw new Exception("Failed to add torrent to Transmission.");
            }

            _logger.LogInformation("Added torrent {TorrentName} to Transmission", newTorrentInfo.Name);
            return newTorrentInfo.ID.Value.ToString(CultureInfo.InvariantCulture);
        }

        public async Task<object> GetProgressAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            if (!int.TryParse(downloadId, out var torrentId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            var torrentsInfo = await client.TorrentGetAsync(new[] { torrentId }, TorrentFields.ALL_FIELDS, ct);
            return torrentsInfo.Torrents.FirstOrDefault();
        }

        public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            if (!int.TryParse(downloadId, out var torrentId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            var torrentsInfo = await client.TorrentGetAsync(new[] { torrentId }, new[] { "downloadDir" }, ct);
            return torrentsInfo.Torrents.FirstOrDefault()?.DownloadDir;
        }

        public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            if (!int.TryParse(downloadId, out var torrentId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            var torrentsInfo = await client.TorrentGetAsync(new[] { torrentId }, new[] { "downloadDir", "files", "fileStats" }, ct);
            var torrent = torrentsInfo.Torrents.FirstOrDefault();

            if (torrent?.Files == null || torrent.FileStats == null || torrent.DownloadDir == null)
                return Array.Empty<FileInfo>();

            return torrent.Files
                .Where((file, index) => torrent.FileStats[index].BytesCompleted == file.Length)
                .Select(file => new FileInfo(Path.Combine(torrent.DownloadDir, file.Name)))
                .ToArray();
        }

        public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
        {
            var client = GetClient();
            if (!int.TryParse(downloadId, out var torrentId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            await client.TorrentRemoveAsync(new[] { torrentId }, deleteFiles, ct);
            _logger.LogInformation("Removed torrent {DownloadId} from Transmission", downloadId);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct)
        {
            try
            {
                var client = GetClient();
                await client.GetSessionInformationAsync(ct);
                _logger.LogInformation("Transmission connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transmission connection test failed");
                return false;
            }
        }
    }
}
