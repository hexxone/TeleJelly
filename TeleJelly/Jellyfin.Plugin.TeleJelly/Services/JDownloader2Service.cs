using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JDownloader;
using JDownloader.Models;
using JDownloader.Models.Devices;
using JDownloader.Models.DownloadsV2;
using JDownloader.Models.Linkgrabber;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public class JDownloader2Service : IHostedDownloadService
    {
        private readonly ILogger _logger;
        private readonly JDownloader2Settings _config;
        private readonly JDownloaderClient _client;
        private DeviceData? _device;

        public JDownloader2Service(ILogger<JDownloader2Service> logger, PluginConfiguration config)
        {
            _logger = logger;
            _config = config.DownloadManager.HostedServices.JDownloader2;
            _client = new JDownloaderClient(new JDownloaderClientOptions { AppKey = "TeleJelly" });
        }

        public string ServiceName => "JDownloader2";
        public bool IsEnabled => _config.Enabled;

        private async Task<DeviceData> GetDeviceClient(CancellationToken ct)
        {
            if (_device != null) return _device;

            await _client.ConnectAsync(_config.Email, _config.Password, ct);
            if (!_client.IsConnected)
                throw new Exception("Failed to connect to My.JDownloader API. Check email and password.");

            var devices = await _client.ListDevicesAsync(ct);
            _device = devices.Devices.FirstOrDefault(d => d.Name == _config.DeviceName);
            if (_device == null)
                throw new Exception($"JDownloader device '{_config.DeviceName}' not found.");

            _client.SetWorkingDevice(_device);
            return _device;
        }

        public bool CanHandle(string linkOrFile)
        {
            return !string.IsNullOrEmpty(linkOrFile) &&
                   (Uri.TryCreate(linkOrFile, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"));
        }

        public async Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct)
        {
            await GetDeviceClient(ct);

            var result = await _client.LinkGrabber.AddLinksAsync(new AddLinksQuery
            {
                Links = linkOrFile,
                Autostart = true
            }, ct);

            if (!result)
            {
                _logger.LogError("Failed to add links to JDownloader.");
                throw new Exception("Failed to add links to JDownloader.");
            }

            _logger.LogInformation("Sent links to JDownloader2. Waiting for package to be created...");
            await Task.Delay(5000, ct);

            var packages = await _client.Downloads.QueryPackagesAsync(new DownloadPackageQuery(), ct);
            var newPackage = packages.OrderByDescending(p => p.Added).FirstOrDefault();

            if (newPackage != null)
            {
                _logger.LogInformation("Found new JDownloader package: {PackageName}", newPackage.Name);
                return newPackage.Uuid.ToString(CultureInfo.InvariantCulture);
            }

            _logger.LogError("Could not find the newly added package in JDownloader.");
            throw new Exception("Could not find the newly added package in JDownloader.");
        }

        public async Task<object> GetProgressAsync(string downloadId, CancellationToken ct)
        {
            await GetDeviceClient(ct);
            if (!long.TryParse(downloadId, NumberStyles.Any, CultureInfo.InvariantCulture, out var packageId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            var packages = await _client.Downloads.QueryPackagesAsync(new DownloadPackageQuery
            {
                PackageUuids = new[] { packageId }
            }, ct);
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
            if (await GetProgressAsync(downloadId, ct) is FilePackage package)
            {
                if (package.Status == "Finished")
                {
                    var links = await _client.Downloads.QueryLinksAsync(new DownloadLinkQuery
                    {
                        PackageUuids = new[] { package.Uuid }
                    }, ct);
                    return links.Select(link => new FileInfo(Path.Combine(package.SaveTo, link.Name))).ToArray();
                }
            }
            return Array.Empty<FileInfo>();
        }

        public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
        {
            await GetDeviceClient(ct);
            if (!long.TryParse(downloadId, NumberStyles.Any, CultureInfo.InvariantCulture, out var packageId))
                throw new ArgumentException("Invalid downloadId format", nameof(downloadId));

            await _client.Downloads.RemoveLinksAsync(null, new[] { packageId }, deleteFiles, ct);
            _logger.LogInformation("Removed download {DownloadId} from JDownloader2", downloadId);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct)
        {
            try
            {
                await GetDeviceClient(ct);
                await _client.Device.PingAsync(ct);
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
            _logger.LogWarning("Client-side DLC password extraction is not supported. Please send the DLC file to JDownloader directly.");
            return Task.FromResult<string?>(null);
        }
    }
}
