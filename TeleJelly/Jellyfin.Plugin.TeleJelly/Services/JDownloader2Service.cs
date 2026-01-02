using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Microsoft.Extensions.Logging;
using My.JDownloader.Api;
using My.JDownloader.Api.Models.Devices.Response;
using My.JDownloader.Api.Models.DownloadsV2.Request;
using My.JDownloader.Api.Models.DownloadsV2.Response;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public class JDownloader2Service : IHostedDownloadService
    {
        private readonly ILogger _logger;
        private readonly JDownloader2Settings _config;

        public JDownloader2Service(ILogger<JDownloader2Service> logger, PluginConfiguration config)
        {
            _logger = logger;
            _config = config.DownloadManager.HostedServices.JDownloader2;
        }

        public string ServiceName => "JDownloader2";
        public bool IsEnabled => _config.Enabled;

        private async Task<Device> GetDeviceClient()
        {
            var jdClient = new JDownloaderClient();
            var isConnected = await jdClient.ConnectAsync(_config.Email, _config.Password);
            if (!isConnected)
            {
                throw new Exception("Failed to connect to My.JDownloader API. Check email and password.");
            }

            var device = jdClient.GetDevice(_config.DeviceName);
            if (device == null)
            {
                throw new Exception($"JDownloader device '{_config.DeviceName}' not found.");
            }

            return device;
        }

        public bool CanHandle(string linkOrFile)
        {
            return !string.IsNullOrEmpty(linkOrFile) &&
                   (Uri.TryCreate(linkOrFile, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"));
        }

        public async Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct)
        {
            var device = await GetDeviceClient();
            var query = new AddLinksQuery
            {
                Links = linkOrFile,
                Autostart = true,
                Priority = Priority.Default
            };

            await device.Links.AddLinksAsync(query);
            _logger.LogInformation("Sent links to JDownloader2. Waiting for package to be created...");

            // JDownloader API is asynchronous. We need to wait and query for the package.
            await Task.Delay(5000, ct); // Wait 5 seconds for the package to be created.

            var packages = await device.Downloads.GetPackagesAsync(new PackageQueryRequest { MaxResults = 10 });
            var newPackage = packages.OrderByDescending(p => p.Added).FirstOrDefault();

            if (newPackage != null)
            {
                _logger.LogInformation("Found new JDownloader package: {PackageName}", newPackage.Name);
                return newPackage.Uuid.ToString();
            }

            _logger.LogError("Could not find the newly added package in JDownloader.");
            throw new Exception("Could not find the newly added package in JDownloader.");
        }

        public async Task<object> GetProgressAsync(string downloadId, CancellationToken ct)
        {
            var device = await GetDeviceClient();
            var packagesQuery = new PackageQueryRequest
            {
                PackageUuids = new[] { Convert.ToInt64(downloadId) },
                MaxResults = 1
            };
            var packages = await device.Downloads.GetPackagesAsync(packagesQuery);
            return packages.FirstOrDefault();
        }

        public async Task<string> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
        {
            if (await GetProgressAsync(downloadId, ct) is FilePackageResponse package)
            {
                return package.SaveTo;
            }
            return null;
        }

        public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
        {
            if (await GetProgressAsync(downloadId, ct) is FilePackageResponse package)
            {
                if (package.Finished)
                {
                     var linksQuery = new LinkQueryRequest
                     {
                         PackageUuids = new[] { package.Uuid }
                     };
                     var links = await (await GetDeviceClient()).Downloads.GetLinksAsync(linksQuery);
                     return links.Select(link => new FileInfo(Path.Combine(package.SaveTo, link.Name))).ToArray();
                }
            }
            return Array.Empty<FileInfo>();
        }

        public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
        {
            var device = await GetDeviceClient();
            var action = deleteFiles ? CleanUpAction.DELETE_ALL : CleanUpAction.REMOVE_ONLY;
            await device.Downloads.RemovePackagesAsync(new[] { Convert.ToInt64(downloadId) }, action);
            _logger.LogInformation("Removed download {DownloadId} from JDownloader2", downloadId);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct)
        {
            try
            {
                await GetDeviceClient();
                _logger.LogInformation("JDownloader2 connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JDownloader2 connection test failed");
                return false;
            }
        }

        public Task<string> ExtractPasswordFromDlcAsync(byte[] dlcContent, CancellationToken ct)
        {
            _logger.LogWarning("Client-side DLC password extraction is not supported. Please send the DLC file to JDownloader directly.");
            return Task.FromResult<string>(null);
        }
    }
}
