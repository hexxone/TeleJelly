using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;
using Microsoft.Extensions.Logging;
using Transmission.API.RPC;
using Transmission.API.RPC.Entity;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;

internal sealed class TransmissionService : ITorrentDownloadService
{
    private static TransmissionSettings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.TorrentServices.Transmission;

    private readonly ILogger _logger;

    public TransmissionService(ILogger<TransmissionService> logger)
    {
        _logger = logger;
    }

    public string ServiceName => "Transmission";

    public bool IsEnabled => Config?.Enabled ?? false;

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
        var newTorrent = new NewTorrent
        {
            Filename = linkOrMagnet,
            Paused = false,
            DownloadDirectory = Config?.StagingPath
        };
        var newTorrentInfo = await client.TorrentAddAsync(newTorrent);

        if (newTorrentInfo?.ID == null)
        {
            _logger.LogError("Failed to add torrent to Transmission or retrieve its ID.");
            throw new Exception("Failed to add torrent to Transmission.");
        }

        _logger.LogInformation("Added torrent {TorrentName} to Transmission", newTorrentInfo.Name);

        return newTorrentInfo.ID.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        var client = GetClient();
        if (!int.TryParse(downloadId, out var torrentId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var torrentsInfo = await client.TorrentGetAsync(TorrentFields.ALL_FIELDS, torrentId);

        return torrentsInfo.Torrents.FirstOrDefault();
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        var client = GetClient();
        if (!int.TryParse(downloadId, out var torrentId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var torrentsInfo = await client.TorrentGetAsync(["downloadDir"], torrentId);

        return torrentsInfo.Torrents.FirstOrDefault()?.DownloadDir;
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        var client = GetClient();
        if (!int.TryParse(downloadId, out var torrentId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var torrentsInfo = await client.TorrentGetAsync(["downloadDir", "files", "fileStats"], torrentId);
        var torrent = torrentsInfo.Torrents.FirstOrDefault();

        if (torrent?.Files == null || torrent.FileStats == null || torrent.DownloadDir == null)
        {
            return [];
        }

        return torrent.Files
            .Where((file, index) => torrent.FileStats[index].BytesCompleted == file.Length)
            .Select(file => new FileInfo(Path.Combine(torrent.DownloadDir, file.Name)))
            .ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        var client = GetClient();
        if (!int.TryParse(downloadId, out var torrentId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        client.TorrentRemoveAsync([torrentId], deleteFiles);

        _logger.LogInformation("Removed torrent {DownloadId} from Transmission", downloadId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var client = GetClient();
            var info = await client.GetSessionInformationAsync();
            var success = info != null;
            if (success)
            {
                _logger.LogInformation("Transmission connection test successful");
            }
            else
            {
                _logger.LogError("Transmission connection test returned null");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission connection test failed");
        }

        return false;
    }

    private Client GetClient()
    {
        var config = Config;
        if (config == null)
        {
            throw new InvalidOperationException("Transmission service is not configured");
        }

        var url = $"http://{config.Host}:{config.Port}/transmission/rpc";
        return new Client(url, null, config.Username, config.Password);
    }
}
