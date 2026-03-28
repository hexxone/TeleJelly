using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class PyLoadService : IHostedDownloadService, IDisposable
{
    private static PyLoadSettings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.HostedServices.PyLoad;

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private string? _authToken;

    public PyLoadService(ILogger<PyLoadService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public string ServiceName => "pyLoad";

    public bool IsEnabled => Config?.Enabled ?? false;

    public bool CanHandle(string linkOrFile)
    {
        if (string.IsNullOrWhiteSpace(linkOrFile))
        {
            return false;
        }

        return SplitLinks(linkOrFile)
            .All(link => Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == "http" || uri.Scheme == "https"));
    }

    public async Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct)
    {
        Debug.Assert(Config != null, nameof(PyLoadSettings) + " != null");

        await EnsureAuthenticatedAsync(ct);

        var payload = new
        {
            name = $"TeleJelly_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            links = SplitLinks(linkOrFile),
            dest = Config.StagingPath
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            GetApiUrl("/api/addPackage"),
            content,
            ct
        );

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync(ct);
        var packageId = JsonSerializer.Deserialize<int>(responseText);

        _logger.LogInformation("Added package {PackageId} to pyLoad", packageId);
        return packageId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!int.TryParse(downloadId, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var response = await _httpClient.GetAsync(
            GetApiUrl($"/api/getPackageData/{packageId}"),
            ct
        );

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PyLoadPackageInfo>(json);
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        var progress = await GetProgressAsync(downloadId, ct);
        return (progress as PyLoadPackageInfo)?.Folder;
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!int.TryParse(downloadId, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var packageInfo = await GetProgressAsync(downloadId, ct) as PyLoadPackageInfo;
        if (packageInfo?.Folder == null)
        {
            return [];
        }

        var response = await _httpClient.GetAsync(
            GetApiUrl($"/api/getPackageInfo/{packageId}"),
            ct
        );

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var detailedInfo = JsonSerializer.Deserialize<PyLoadDetailedPackageInfo>(json);

        if (detailedInfo?.Links == null)
        {
            return [];
        }

        return detailedInfo.Links
            .Where(l => l.Status == "finished")
            .Select(l => new FileInfo(Path.Combine(packageInfo.Folder, l.Name)))
            .ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        await EnsureAuthenticatedAsync(ct);

        if (!int.TryParse(downloadId, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var content = new StringContent(
            JsonSerializer.Serialize(new[] { packageId }),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            GetApiUrl("/api/deletePackages"),
            content,
            ct
        );

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Removed download {DownloadId} from pyLoad", downloadId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            await EnsureAuthenticatedAsync(ct);

            var response = await _httpClient.GetAsync(
                GetApiUrl("/api/getServerVersion"),
                ct
            );

            var success = response.IsSuccessStatusCode;
            if (success)
            {
                var version = await response.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("pyLoad connection test successful (version: {Version})", version);
            }
            else
            {
                _logger.LogError("pyLoad connection test failed with status code: {StatusCode}", response.StatusCode);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "pyLoad connection test failed");
        }

        return false;
    }

    public Task<string?> ExtractPasswordFromDlcAsync(byte[] dlcContent, CancellationToken ct)
    {
        try
        {
            // DLC files are Base64-encoded XML
            var base64String = Encoding.UTF8.GetString(dlcContent);
            var xmlBytes = Convert.FromBase64String(base64String);
            var xmlString = Encoding.UTF8.GetString(xmlBytes);

            var doc = XDocument.Parse(xmlString);
            var passwordElement = doc.Descendants("passwords").FirstOrDefault();

            if (passwordElement != null && !string.IsNullOrEmpty(passwordElement.Value))
            {
                _logger.LogInformation("Successfully extracted password from DLC file");
                return Task.FromResult<string?>(passwordElement.Value);
            }

            _logger.LogInformation("No password found in DLC file");
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract password from DLC file");
            return Task.FromResult<string?>(null);
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        Debug.Assert(Config != null, nameof(PyLoadSettings) + " != null");

        if (_authToken != null)
        {
            return;
        }

        var payload = new
        {
            username = Config.Username,
            password = Config.Password
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            GetApiUrl("/api/login"),
            content,
            ct
        );

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync(ct);
        _authToken = JsonSerializer.Deserialize<string>(responseText);

        if (string.IsNullOrEmpty(_authToken))
        {
            _logger.LogError("pyLoad authentication failed: No token returned");
            throw new Exception("Failed to authenticate with pyLoad");
        }

        _httpClient.DefaultRequestHeaders.Add("Authorization", _authToken);
        _logger.LogInformation("Successfully authenticated with pyLoad");
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

    private static string[] SplitLinks(string linkOrFile)
    {
        return linkOrFile
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private class PyLoadPackageInfo
    {
        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("folder")]
        public string Folder { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("linksdone")]
        public int LinksDone { get; set; }

        [JsonPropertyName("links")]
        public int Links { get; set; }
    }

    private class PyLoadDetailedPackageInfo
    {
        [JsonPropertyName("links")]
        public List<PyLoadLinkInfo>? Links { get; set; }
    }

    private class PyLoadLinkInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}
