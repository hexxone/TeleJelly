using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using JDownloader;
using JDownloader.Model;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal sealed class JDownloader2Service : IHostedDownloadService
{
    private static JDownloader2Settings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.HostedServices.JDownloader2;

    private readonly JDownloaderClient _client;
    private readonly ILogger _logger;
    private DeviceData? _device;

    public JDownloader2Service(ILogger<JDownloader2Service> logger)
    {
        _logger = logger;
        _client = new JDownloaderClient(new JDownloaderClientOptions { AppKey = "TeleJelly" });
    }

    public string ServiceName => "JDownloader2";

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
        await GetDeviceClient(ct);
        var packageName = $"TeleJelly_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        var result = await _client.LinkGrabberV2.AddLinks(new AddLinksQuery
        {
            Links = linkOrFile,
            AutoStart = true,
            PackageName = packageName,
            DestinationFolder = Config?.StagingPath
        });

        if (result == null)
        {
            _logger.LogError("Failed to add links to JDownloader.");
            throw new Exception("Failed to add links to JDownloader.");
        }

        _logger.LogInformation("Sent links to JDownloader2. Polling for package creation...");
        var newPackage = await WaitForPackageAsync(result.Id, packageName, ct);

        if (newPackage != null)
        {
            _logger.LogInformation("Found new JDownloader package: {PackageName}", newPackage.Name);
            return newPackage.Uuid.ToString(CultureInfo.InvariantCulture);
        }

        _logger.LogError("Could not find the newly added package in JDownloader.");
        throw new Exception("Could not find the newly added package in JDownloader.");
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        if (!long.TryParse(downloadId, NumberStyles.Any, CultureInfo.InvariantCulture, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var packages = await _client.DownloadsV2.QueryPackages(new PackageQuery([packageId]));

        return packages.FirstOrDefault();
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        if (await GetProgressAsync(downloadId, ct) is FilePackage package)
        {
            return package.SaveTo;
        }

        return null;
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        if (await GetProgressAsync(downloadId, ct) is not FilePackage package || package.Status != "Finished")
        {
            return [];
        }

        var links = await _client.DownloadsV2.QueryLinks(new LinkQuery([package.UUID]));

        return links.Select(link => new FileInfo(Path.Combine(package.SaveTo, link.Name))).ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        if (!long.TryParse(downloadId, NumberStyles.Any, CultureInfo.InvariantCulture, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        await _client.DownloadsV2.RemoveLinks(null, [packageId]);

        _logger.LogInformation("Removed download {DownloadId} from JDownloader2", downloadId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            await GetDeviceClient(ct);
            await _client.Device.Ping();
            _logger.LogInformation("JDownloader2 connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JDownloader2 connection test failed");
            return false;
        }
    }

    public Task<string?> ExtractPasswordFromDlcAsync(byte[] dlcContent, CancellationToken ct)
    {
        if (TeleJellyPlugin.Instance?.Configuration.DownloadManager.Extraction.ExtractPasswordsFromDlc == false)
        {
            _logger.LogInformation("DLC password extraction is disabled for JDownloader2");
            return Task.FromResult<string?>(null);
        }

        try
        {
            var base64String = Encoding.UTF8.GetString(dlcContent);
            var xmlBytes = Convert.FromBase64String(base64String);
            var xmlString = Encoding.UTF8.GetString(xmlBytes);

            var document = XDocument.Parse(xmlString);
            var passwordElement = document.Descendants("passwords").FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(passwordElement?.Value))
            {
                _logger.LogInformation("Successfully extracted password from DLC file for JDownloader2");
                return Task.FromResult<string?>(passwordElement.Value);
            }

            _logger.LogInformation("No password found in DLC file for JDownloader2");
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract password from DLC file for JDownloader2");
            return Task.FromResult<string?>(null);
        }
    }

    private static IEnumerable<string> SplitLinks(string linkOrFile)
    {
        return linkOrFile
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<DeviceData> GetDeviceClient(CancellationToken ct)
    {
        if (_device != null)
        {
            return _device;
        }

        if (Config == null)
        {
            throw new Exception("No JDownloader API configured.");
        }

        await _client.Connect(Config.Email, Config.Password);
        if (!_client.IsConnected)
        {
            throw new Exception("Failed to connect to My.JDownloader API. Check email and password.");
        }

        var devices = await _client.ListDevices();
        _device = devices.Devices.FirstOrDefault(d => d.Name == Config.DeviceName);
        if (_device == null)
        {
            throw new Exception($"JDownloader device '{Config.DeviceName}' not found.");
        }

        _client.SetWorkingDevice(_device);
        return _device;
    }

    private async Task<CrawledPackage?> WaitForPackageAsync(long resultId, string packageName, CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var collected = await _client.LinkGrabberV2.QueryPackages(new CrawledPackageQuery([resultId])) ?? [];
            var newPackage = collected
                .OrderByDescending(package => package.Uuid)
                .FirstOrDefault(package => string.Equals(package.Name, packageName, StringComparison.OrdinalIgnoreCase))
                ?? collected.OrderByDescending(package => package.Uuid).FirstOrDefault();

            if (newPackage != null)
            {
                return newPackage;
            }

            if (attempt < maxPollAttempts)
            {
                await Task.Delay(pollDelay, ct);
            }
        }

        return null;
    }
}
