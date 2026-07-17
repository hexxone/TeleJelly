using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JDownloader;
using JDownloader.Model;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal sealed class MyJDownloader2Service : IJDownloader2ServiceBackend
{
    private static JDownloader2Settings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.HostedServices.JDownloader2;

    private readonly JDownloaderClient _client;
    private readonly ILogger _logger;
    private DeviceData? _device;

    public MyJDownloader2Service(ILogger logger)
    {
        _logger = logger;
        _client = new JDownloaderClient(new JDownloaderClientOptions { AppKey = "TeleJelly" });
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

    public async Task<string> AddContainerAsync(byte[] content, string containerType, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var dataUrl = $"data:application/{containerType.ToLowerInvariant()};base64,{Convert.ToBase64String(content)}";
        var job = await _client.LinkGrabberV2.AddLinks(new AddLinksQuery
        {
            DataURLs = [dataUrl],
            AssignJobID = true,
            AutoStart = false,
            DestinationFolder = Config?.StagingPath
        });
        if (job == null)
        {
            throw new Exception("JDownloader did not return a crawler job for the DLC container.");
        }

        _logger.LogInformation(
            "Submitted {ContainerType} container as JDownloader crawler job {CrawlerJobId}",
            containerType,
            job.Id);
        return job.Id.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<JDownloaderContainerImportProgress> GetContainerImportProgressAsync(string crawlerJobId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var jobId = ParseSingleId(crawlerJobId, nameof(crawlerJobId));
        var jobs = await _client.LinkGrabberV2.QueryLinkCrawlerJobs(new LinkCrawlerJobsQuery
        {
            JobIds = [jobId],
            CollectorInfo = true
        }) ?? [];
        var job = jobs.FirstOrDefault();
        var links = await QueryLinksForJobAsync(jobId);

        if (job is { Crawling: false, Checking: false, Unhandled: 0 })
        {
            if (links.Count == 0)
            {
                return new JDownloaderContainerImportProgress(
                    false,
                    true,
                    job.Crawled,
                    job.Broken,
                    job.Filtered,
                    $"DLC resolved no usable links ({job.Broken} broken, {job.Filtered} filtered).");
            }

            return new JDownloaderContainerImportProgress(true, false, links.Count, job.Broken, job.Filtered, $"DLC resolution finished with {links.Count} link(s)");
        }

        if (job == null && links.Count > 0)
        {
            return new JDownloaderContainerImportProgress(true, false, links.Count, 0, 0, $"DLC resolution finished with {links.Count} link(s)");
        }

        var crawled = Math.Max(job?.Crawled ?? 0, links.Count);
        return new JDownloaderContainerImportProgress(
            false,
            false,
            crawled,
            job?.Broken ?? 0,
            job?.Filtered ?? 0,
            $"JDownloader LinkGrabber is resolving the DLC ({crawled} link(s) found so far)");
    }

    public async Task<string> CompleteContainerImportAsync(string crawlerJobId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var jobId = ParseSingleId(crawlerJobId, nameof(crawlerJobId));
        var links = await QueryLinksForJobAsync(jobId);
        var linkIds = links
            .Select(link => link.Uuid)
            .Where(linkId => linkId != 0)
            .Distinct()
            .ToArray();
        var linkGrabberPackageIds = links
            .Select(link => link.PackageUUID)
            .Where(packageId => packageId != 0)
            .Distinct()
            .ToArray();
        if (linkIds.Length == 0 || linkGrabberPackageIds.Length == 0)
        {
            throw new Exception("JDownloader finished resolving the DLC but no job-owned LinkGrabber links were found.");
        }

        if (!string.IsNullOrWhiteSpace(Config?.StagingPath))
        {
            await _client.LinkGrabberV2.SetDownloadDirectory(linkGrabberPackageIds, Config.StagingPath);
        }

        await _client.LinkGrabberV2.MoveToDownloadList(linkIds, []);
        var downloadPackageIds = await WaitForDownloadPackageIdsAsync(linkIds, ct);
        _logger.LogInformation(
            "Moved {LinkCount} link(s) from DLC crawler job {CrawlerJobId} to {PackageCount} JDownloader Downloads package(s)",
            linkIds.Length,
            jobId,
            downloadPackageIds.Length);
        return FormatPackageIds(downloadPackageIds);
    }

    public async Task CancelContainerImportAsync(string crawlerJobId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        await _client.LinkGrabberV2.Abort(ParseSingleId(crawlerJobId, nameof(crawlerJobId)));
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var packages = await _client.DownloadsV2.QueryPackages(new PackageQuery(ParsePackageIds(downloadId))) ?? [];
        if (packages.Count == 0)
        {
            return null;
        }

        var links = await _client.DownloadsV2.QueryLinks(new LinkQuery(packages.Select(package => package.UUID).ToArray())) ?? [];
        return new JDownloaderAggregateProgress
        {
            BytesLoaded = packages.Sum(package => package.BytesLoaded),
            BytesTotal = packages.Sum(package => package.BytesTotal),
            Links = links.Count,
            LinksDone = links.Count(link => link.Finished),
            SaveTo = packages.Select(package => package.SaveTo).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
            Status = packages.All(package => package.Finished || string.Equals(package.Status, "Finished", StringComparison.OrdinalIgnoreCase))
                ? "Finished"
                : string.Join(", ", packages.Select(package => package.Status).Where(status => !string.IsNullOrWhiteSpace(status)).Distinct())
        };
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var packages = await _client.DownloadsV2.QueryPackages(new PackageQuery(ParsePackageIds(downloadId))) ?? [];
        return packages.Select(package => package.SaveTo).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        var packages = await _client.DownloadsV2.QueryPackages(new PackageQuery(ParsePackageIds(downloadId))) ?? [];
        if (packages.Count == 0 || packages.Any(package => !package.Finished && !string.Equals(package.Status, "Finished", StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        var files = new System.Collections.Generic.List<FileInfo>();
        foreach (var package in packages)
        {
            var links = await _client.DownloadsV2.QueryLinks(new LinkQuery([package.UUID])) ?? [];
            files.AddRange(links.Where(link => link.Finished).Select(link => new FileInfo(Path.Combine(package.SaveTo, link.Name))));
        }

        return files.ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        await GetDeviceClient(ct);
        await _client.DownloadsV2.RemoveLinks(null, ParsePackageIds(downloadId));

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

    public void Dispose()
    {
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

    private Task<System.Collections.Generic.List<CrawledLink>> QueryLinksForJobAsync(long jobId)
    {
        return _client.LinkGrabberV2.QueryLinks(new CrawledLinkQuery { JobUUIDs = [jobId] });
    }

    private async Task<long[]> WaitForDownloadPackageIdsAsync(long[] linkIds, CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(1);
        var expectedLinkIds = linkIds.ToHashSet();

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            // JDownloader.NET 1.1.0 does not expose LinkQuery.linkUUIDs, so query
            // the download list and apply the exact UUID filter client-side.
            var allDownloadLinks = await _client.DownloadsV2.QueryLinks(new LinkQuery([])) ?? [];
            var movedLinks = allDownloadLinks
                .Where(link => expectedLinkIds.Contains(link.UUID))
                .ToArray();
            if (movedLinks.Select(link => link.UUID).ToHashSet().SetEquals(expectedLinkIds))
            {
                var packageIds = movedLinks
                    .Select(link => link.PackageUUID)
                    .Where(packageId => packageId != 0)
                    .Distinct()
                    .ToArray();
                if (packageIds.Length > 0)
                {
                    return packageIds;
                }
            }

            if (attempt < maxPollAttempts)
            {
                await Task.Delay(pollDelay, ct);
            }
        }

        throw new Exception($"JDownloader moved the DLC links but did not expose all {linkIds.Length} link(s) in Downloads.");
    }

    private static long ParseSingleId(string id, string parameterName)
    {
        if (!long.TryParse(id, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException("Invalid JDownloader ID format", parameterName);
        }

        return value;
    }

    private static long[] ParsePackageIds(string downloadId)
    {
        const string prefix = "packages:";
        var value = downloadId.StartsWith(prefix, StringComparison.Ordinal) ? downloadId[prefix.Length..] : downloadId;
        var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => ParseSingleId(id, nameof(downloadId)))
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        return ids;
    }

    private static string FormatPackageIds(long[] packageIds)
    {
        return "packages:" + string.Join(',', packageIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
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

    private async Task<CrawledPackage?> WaitForNewLinkGrabberPackageAsync(
        System.Collections.Generic.HashSet<long> existingPackageIds,
        CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var package = (await _client.LinkGrabberV2.QueryPackages(new CrawledPackageQuery([])) ?? [])
                .Where(candidate => !existingPackageIds.Contains(candidate.Uuid))
                .OrderByDescending(candidate => candidate.Uuid)
                .FirstOrDefault();
            if (package != null)
            {
                return package;
            }

            if (attempt < maxPollAttempts)
            {
                await Task.Delay(pollDelay, ct);
            }
        }

        return null;
    }
}
