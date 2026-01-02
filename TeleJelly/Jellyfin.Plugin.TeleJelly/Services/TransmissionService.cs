using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
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
            return new Client(_config.Host, _config.Port, _config.Username, _config.Password);
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
            var options = new AddTorrentOptions
            {
                Filename = linkOrMagnet,
                Paused = false
            };
            var newTorrent = await client.TorrentAddAsync(options, ct);
            _logger.LogInformation("Added torrent {TorrentName} to Transmission", newTorrent.Name);
            return newTorrent.HashString;
        }

        public async Task<object> GetProgressAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            var torrentInfo = await client.TorrentGetAsync(new[] { downloadId }, TorrentFields.ALL_FIELDS, ct);
            return torrentInfo.Torrents.FirstOrDefault();
        }

        public async Task<string> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            var torrentInfo = await client.TorrentGetAsync(new[] { downloadId }, new[] { "downloadDir" }, ct);
            return torrentInfo.Torrents.FirstOrDefault()?.DownloadDir;
        }

        public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
        {
            var client = GetClient();
            var torrentInfo = await client.TorrentGetAsync(new[] { downloadId }, new[] { "downloadDir", "files", "fileStats" }, ct);
            var torrent = torrentInfo.Torrents.FirstOrDefault();

            if (torrent == null)
            {
                return Array.Empty<FileInfo>();
            }

            return torrent.Files
                .Where((file, index) => torrent.FileStats[index].BytesCompleted == file.Length)
                .Select(file => new FileInfo(Path.Combine(torrent.DownloadDir, file.Name)))
                .ToArray();
        }

        public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
        {
            var client = GetClient();
            await client.TorrentRemoveAsync(new[] { downloadId }, deleteFiles, ct);
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
