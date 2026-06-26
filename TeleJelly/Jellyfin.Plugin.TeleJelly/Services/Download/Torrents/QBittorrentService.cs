using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;

internal sealed class QBittorrentService : ITorrentDownloadService, IDisposable
{
    private static QBittorrentSettings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.TorrentServices.QBittorrent;

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private string? _authCookie;

    public QBittorrentService(ILogger<QBittorrentService> logger)
    {
        _logger = logger;
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookieContainer };
        _httpClient = new HttpClient(handler);
    }

    public string ServiceName => "qBittorrent";

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
        Debug.Assert(Config != null, nameof(QBittorrentSettings) + " != null");

        await EnsureAuthenticatedAsync(ct);

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "urls", linkOrMagnet },
            { "savepath", Config.StagingPath }
        });

        var response = await _httpClient.PostAsync(
            GetApiUrl("/api/v2/torrents/add"),
            content,
            ct
        );

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Added torrent to qBittorrent: {Link}", linkOrMagnet);

        // qBittorrent doesn't return the hash directly, we need to find it
        // Wait a moment for the torrent to appear
        await Task.Delay(1000, ct);

        // Get all torrents and find the most recent one
        var torrents = await GetAllTorrentsAsync(ct);
        var newestTorrent = torrents.OrderByDescending(t => t.AddedOn).FirstOrDefault();

        if (newestTorrent?.Hash == null)
        {
            _logger.LogError("Failed to retrieve torrent hash after adding");
            throw new Exception("Failed to add torrent to qBittorrent");
        }

        return newestTorrent.Hash;
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        var response = await _httpClient.GetAsync(
            GetApiUrl($"/api/v2/torrents/info?hashes={downloadId}"),
            ct
        );

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var torrents = JsonSerializer.Deserialize<List<QBittorrentTorrentInfo>>(json);

        return torrents?.FirstOrDefault();
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        var progress = await GetProgressAsync(downloadId, ct);
        return (progress as QBittorrentTorrentInfo)?.SavePath;
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        var torrentInfo = await GetProgressAsync(downloadId, ct) as QBittorrentTorrentInfo;
        if (torrentInfo?.SavePath == null)
        {
            return [];
        }

        var response = await _httpClient.GetAsync(
            GetApiUrl($"/api/v2/torrents/files?hash={downloadId}"),
            ct
        );

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var files = JsonSerializer.Deserialize<List<QBittorrentFileInfo>>(json);

        if (files == null)
        {
            return [];
        }

        return files
            .Where(f => f.Progress >= 1.0)
            .Select(f => new FileInfo(Path.Combine(torrentInfo.SavePath, f.Name)))
            .ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        var response = await _httpClient.PostAsync(
            GetApiUrl($"/api/v2/torrents/delete?hashes={downloadId}&deleteFiles={deleteFiles.ToString().ToLower()}"),
            null,
            ct
        );

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Removed torrent {DownloadId} from qBittorrent", downloadId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            await EnsureAuthenticatedAsync(ct);

            var response = await _httpClient.GetAsync(
                GetApiUrl("/api/v2/app/version"),
                ct
            );

            var success = response.IsSuccessStatusCode;
            if (success)
            {
                var version = await response.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("qBittorrent connection test successful (version: {Version})", version);
            }
            else
            {
                _logger.LogError("qBittorrent connection test failed with status code: {StatusCode}", response.StatusCode);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "qBittorrent connection test failed");
        }

        return false;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        Debug.Assert(Config != null, nameof(PyLoadSettings) + " != null");

        if (_authCookie != null)
        {
            return;
        }

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "username", Config.Username },
            { "password", Config.Password }
        });

        var response = await _httpClient.PostAsync(
            GetApiUrl("/api/v2/auth/login"),
            content,
            ct
        );

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(ct);
        if (result != "Ok.")
        {
            _logger.LogError("qBittorrent authentication failed: {Result}", result);
            throw new Exception("Failed to authenticate with qBittorrent");
        }

        _authCookie = "authenticated";
        _logger.LogInformation("Successfully authenticated with qBittorrent");
    }

    private async Task<List<QBittorrentTorrentInfo>> GetAllTorrentsAsync(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(
            GetApiUrl("/api/v2/torrents/info"),
            ct
        );

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<QBittorrentTorrentInfo>>(json) ?? [];
    }

    private string GetApiUrl(string endpoint)
    {
        Debug.Assert(Config != null, nameof(PyLoadSettings) + " != null");

        return $"http://{Config.Host}:{Config.Port}{endpoint}";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    [Serializable]
    private class QBittorrentTorrentInfo
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("save_path")]
        public string SavePath { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("added_on")]
        public long AddedOn { get; set; }
    }

    [Serializable]
    private class QBittorrentFileInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("progress")]
        public double Progress { get; set; }
    }
}
