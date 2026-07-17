using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal sealed class LegacyJDownloader2Service : IJDownloader2ServiceBackend
{
    private static JDownloader2Settings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.HostedServices.JDownloader2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public LegacyJDownloader2Service(ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    internal LegacyJDownloader2Service(ILogger logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _httpClient = new HttpClient(handler);
    }

    public async Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct)
    {
        if (Config == null)
        {
            throw new Exception("No JDownloader API configured.");
        }

        var packageName = $"TeleJelly_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var data = await InvokeApiAsync(
            "/linkgrabberv2/addLinks",
            [
                new
                {
                    links = linkOrFile,
                    autostart = true,
                    packageName,
                    destinationFolder = Config.StagingPath
                }
            ],
            ct);

        var crawlerJobId = TryReadResultId(data);

        _logger.LogInformation(
            "Sent links to JDownloader2 legacy API as crawler job {CrawlerJobId}. Polling for package {PackageName}...",
            crawlerJobId,
            packageName);

        // JDownloader silently deduplicates a URL that is already present in
        // LinkGrabber. In that case no new package is created, so surface the
        // existing link-level rejection instead of timing out with "not found".
        await ThrowIfExistingLinkWasRejectedAsync(linkOrFile, ct);

        var newPackage = await WaitForPackageAsync(packageName, ct);

        if (newPackage != null)
        {
            var progress = await GetPackageAsync(newPackage.Uuid.ToString(CultureInfo.InvariantCulture), ct);
            if (progress?.Status.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new DownloadRejectedException(progress.Status["Failed:".Length..].Trim());
            }

            _logger.LogInformation("Found new JDownloader legacy package: {PackageName}", newPackage.Name);
            return newPackage.Uuid.ToString(CultureInfo.InvariantCulture);
        }

        _logger.LogError("Could not find the newly added package in JDownloader legacy API.");
        throw new Exception("Could not find the newly added package in JDownloader legacy API.");
    }

    public async Task<string> AddContainerAsync(byte[] content, string containerType, CancellationToken ct)
    {
        if (Config == null)
        {
            throw new Exception("No JDownloader API configured.");
        }

        var correlationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var dataUrl = $"data:application/{containerType.ToLowerInvariant()};base64,{Convert.ToBase64String(content)}";
        var data = await InvokeApiAsync(
            "/linkgrabberv2/addLinks",
            [
                new
                {
                    dataURLs = new[] { dataUrl },
                    assignJobID = true,
                    autostart = false,
                    comment = $"TeleJelly:{correlationId}",
                    destinationFolder = Config.StagingPath
                }
            ],
            ct);

        var crawlerJobId = TryReadResultId(data);
        if (!crawlerJobId.HasValue)
        {
            throw new Exception("JDownloader did not return a crawler job for the DLC container.");
        }

        _logger.LogInformation(
            "Submitted {ContainerType} container as JDownloader crawler job {CrawlerJobId}",
            containerType,
            crawlerJobId.Value);
        return crawlerJobId.Value.ToString(CultureInfo.InvariantCulture);
    }

    public async Task<JDownloaderContainerImportProgress> GetContainerImportProgressAsync(string crawlerJobId, CancellationToken ct)
    {
        var jobId = ParseSingleId(crawlerJobId, nameof(crawlerJobId));
        var data = await InvokeApiAsync(
            "/linkgrabberv2/queryLinkCrawlerJobs",
            [new { jobIds = new[] { jobId }, collectorInfo = true }],
            ct);
        var jobs = data?.Deserialize<LegacyCrawlerJob[]>(JsonOptions) ?? [];
        var job = jobs.FirstOrDefault();
        var links = await QueryLinkGrabberLinksForJobAsync(jobId, ct);

        if (job is { Crawling: false, Checking: false, Unhandled: 0 })
        {
            if (links.Length == 0)
            {
                return new JDownloaderContainerImportProgress(
                    false,
                    true,
                    job.Crawled,
                    job.Broken,
                    job.Filtered,
                    $"DLC resolved no usable links ({job.Broken} broken, {job.Filtered} filtered).");
            }

            return new JDownloaderContainerImportProgress(
                true,
                false,
                links.Length,
                job.Broken,
                job.Filtered,
                $"DLC resolution finished with {links.Length} link(s)");
        }

        if (job == null && links.Length > 0)
        {
            return new JDownloaderContainerImportProgress(true, false, links.Length, 0, 0, $"DLC resolution finished with {links.Length} link(s)");
        }

        var crawled = Math.Max(job?.Crawled ?? 0, links.Length);
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
        var jobId = ParseSingleId(crawlerJobId, nameof(crawlerJobId));
        var links = await QueryLinkGrabberLinksForJobAsync(jobId, ct);
        var linkIds = links
            .Select(link => link.Uuid)
            .Where(linkId => linkId != 0)
            .Distinct()
            .ToArray();
        var linkGrabberPackageIds = links
            .Select(link => link.PackageUuid)
            .Where(packageId => packageId != 0)
            .Distinct()
            .ToArray();
        if (linkIds.Length == 0 || linkGrabberPackageIds.Length == 0)
        {
            throw new Exception("JDownloader finished resolving the DLC but no job-owned LinkGrabber links were found.");
        }

        if (!string.IsNullOrWhiteSpace(Config?.StagingPath))
        {
            await InvokeApiAsync("/linkgrabberv2/setDownloadDirectory", [Config.StagingPath, linkGrabberPackageIds], ct);
        }

        await InvokeApiAsync("/linkgrabberv2/moveToDownloadlist", [linkIds, Array.Empty<long>()], ct);
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
        var jobId = ParseSingleId(crawlerJobId, nameof(crawlerJobId));
        await InvokeApiAsync("/linkgrabberv2/abort", [jobId], ct);
    }

    public async Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        var packages = await GetPackagesAsync(downloadId, ct);
        if (packages.Length == 0)
        {
            return null;
        }

        return new JDownloaderAggregateProgress
        {
            BytesLoaded = packages.Sum(package => package.BytesLoaded),
            BytesTotal = packages.Sum(package => package.BytesTotal),
            Links = packages.Sum(package => package.Links),
            LinksDone = packages.Sum(package => package.LinksDone),
            SaveTo = packages.Select(package => package.SaveTo).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
            Status = packages.All(IsPackageFinished)
                ? "Finished"
                : string.Join(", ", packages.Select(package => package.Status).Where(status => !string.IsNullOrWhiteSpace(status)).Distinct())
        };
    }

    public async Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        return (await GetPackagesAsync(downloadId, ct))
            .Select(package => package.SaveTo)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    public async Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        var packages = await GetPackagesAsync(downloadId, ct);
        if (packages.Length == 0 || packages.Any(package => !IsPackageFinished(package)))
        {
            return [];
        }

        var files = new List<FileInfo>();
        foreach (var package in packages.Where(package => !string.IsNullOrWhiteSpace(package.SaveTo)))
        {
            files.AddRange((await QueryLinksAsync(package.Uuid, ct))
                .Where(IsLinkFinished)
                .Select(link => new FileInfo(Path.Combine(package.SaveTo, link.Name))));
        }

        return files.ToArray();
    }

    public async Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        var packageIds = ParsePackageIds(downloadId);
        await InvokeApiAsync("/downloadsV2/removeLinks", [Array.Empty<long>(), packageIds], ct);
        _logger.LogInformation("Removed download {DownloadId} from JDownloader2 legacy API", downloadId);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            await InvokeApiAsync("/downloadsV2/queryLinks", [new { }], ct);
            _logger.LogInformation("JDownloader2 legacy API connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JDownloader2 legacy API connection test failed");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<LegacyJDownloaderPackage?> GetPackageAsync(string downloadId, CancellationToken ct)
    {
        if (!long.TryParse(downloadId, NumberStyles.Any, CultureInfo.InvariantCulture, out var packageId))
        {
            throw new ArgumentException("Invalid downloadId format", nameof(downloadId));
        }

        var package = (await QueryPackagesAsync([packageId], ct)).FirstOrDefault();
        var isInLinkGrabber = false;
        if (package == null)
        {
            package = (await QueryLinkGrabberPackagesAsync([packageId], ct)).FirstOrDefault();
            isInLinkGrabber = package != null;
        }

        if (package == null)
        {
            return null;
        }

        var links = isInLinkGrabber
            ? await QueryLinkGrabberLinksAsync(packageId, ct)
            : await QueryLinksAsync(packageId, ct);

        if (isInLinkGrabber && string.IsNullOrWhiteSpace(package.Status))
        {
            package.Status = "Resolving links in LinkGrabber";
        }

        EnrichPackage(package, links);
        return package;
    }

    private async Task<LegacyJDownloaderPackage[]> GetPackagesAsync(string downloadId, CancellationToken ct)
    {
        var packageIds = ParsePackageIds(downloadId);
        var packages = await QueryPackagesAsync(packageIds, ct);
        var missingIds = packageIds.Except(packages.Select(package => package.Uuid)).ToArray();
        if (missingIds.Length > 0)
        {
            packages = packages.Concat(await QueryLinkGrabberPackagesAsync(missingIds, ct)).ToArray();
        }

        foreach (var package in packages)
        {
            var isInDownloads = packageIds.Contains(package.Uuid) &&
                                (await QueryPackagesAsync([package.Uuid], ct)).Length > 0;
            var links = isInDownloads
                ? await QueryLinksAsync(package.Uuid, ct)
                : await QueryLinkGrabberLinksAsync(package.Uuid, ct);
            EnrichPackage(package, links);
        }

        return packages;
    }

    private async Task<LegacyJDownloaderPackage?> WaitForPackageAsync(string packageName, CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // addLinks returns a link-crawler job ID, not a download package UUID.
            // Query all packages and match the unique TeleJelly package name instead.
            var packages = await QueryPackagesAsync([], ct);
            var newPackage = FindPackage(packages, packageName);

            if (newPackage != null)
            {
                return newPackage;
            }

            packages = await QueryLinkGrabberPackagesAsync([], ct);
            newPackage = FindPackage(packages, packageName);

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

    private async Task<LegacyJDownloaderPackage?> WaitForNewLinkGrabberPackageAsync(
        HashSet<long> existingPackageIds,
        CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var newPackage = (await QueryLinkGrabberPackagesAsync([], ct))
                .Where(package => !existingPackageIds.Contains(package.Uuid))
                .OrderByDescending(package => package.Uuid)
                .FirstOrDefault();
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

    private async Task<LegacyJDownloaderPackage[]> QueryPackagesAsync(long[] packageIds, CancellationToken ct)
    {
        var query = CreatePackageQuery(packageIds);

        var data = await InvokeApiAsync("/downloadsV2/queryPackages", [query], ct);
        return data?.Deserialize<LegacyJDownloaderPackage[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderPackage[]> QueryLinkGrabberPackagesAsync(long[] packageIds, CancellationToken ct)
    {
        var query = CreatePackageQuery(packageIds);

        var data = await InvokeApiAsync("/linkgrabberv2/queryPackages", [query], ct);
        return data?.Deserialize<LegacyJDownloaderPackage[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderLink[]> QueryLinksAsync(long packageId, CancellationToken ct)
    {
        var query = new
        {
            packageUUIDs = new[] { packageId },
            bytesLoaded = true,
            bytesTotal = true,
            enabled = true,
            finished = true,
            host = true,
            name = true,
            size = true,
            status = true,
            url = true
        };

        var data = await InvokeApiAsync("/downloadsV2/queryLinks", [query], ct);
        return data?.Deserialize<LegacyJDownloaderLink[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderLink[]> QueryDownloadLinksByIdsAsync(long[] linkIds, CancellationToken ct)
    {
        var query = new
        {
            linkUUIDs = linkIds,
            bytesLoaded = true,
            bytesTotal = true,
            finished = true,
            status = true
        };

        var data = await InvokeApiAsync("/downloadsV2/queryLinks", [query], ct);
        return data?.Deserialize<LegacyJDownloaderLink[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderLink[]> QueryLinkGrabberLinksAsync(long packageId, CancellationToken ct)
    {
        var query = new
        {
            packageUUIDs = new[] { packageId },
            bytesLoaded = true,
            bytesTotal = true,
            enabled = true,
            finished = true,
            host = true,
            name = true,
            size = true,
            status = true,
            url = true
        };

        var data = await InvokeApiAsync("/linkgrabberv2/queryLinks", [query], ct);
        return data?.Deserialize<LegacyJDownloaderLink[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderLink[]> QueryAllLinkGrabberLinksAsync(CancellationToken ct)
    {
        var query = new
        {
            bytesLoaded = true,
            bytesTotal = true,
            enabled = true,
            finished = true,
            host = true,
            name = true,
            packageUUID = true,
            size = true,
            status = true,
            url = true
        };

        var data = await InvokeApiAsync("/linkgrabberv2/queryLinks", [query], ct);
        return data?.Deserialize<LegacyJDownloaderLink[]>(JsonOptions) ?? [];
    }

    private async Task<LegacyJDownloaderLink[]> QueryLinkGrabberLinksForJobAsync(long jobId, CancellationToken ct)
    {
        var query = new
        {
            jobUUIDs = new[] { jobId },
            jobUUID = true,
            packageUUID = true,
            bytesTotal = true,
            enabled = true,
            name = true,
            status = true,
            url = true
        };

        var data = await InvokeApiAsync("/linkgrabberv2/queryLinks", [query], ct);
        return data?.Deserialize<LegacyJDownloaderLink[]>(JsonOptions) ?? [];
    }

    private async Task<long[]> WaitForDownloadPackageIdsAsync(long[] linkIds, CancellationToken ct)
    {
        const int maxPollAttempts = 15;
        var pollDelay = TimeSpan.FromSeconds(1);
        var expectedLinkIds = linkIds.ToHashSet();

        for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var movedLinks = (await QueryDownloadLinksByIdsAsync(linkIds, ct))
                .Where(link => expectedLinkIds.Contains(link.Uuid))
                .ToArray();
            if (movedLinks.Select(link => link.Uuid).ToHashSet().SetEquals(expectedLinkIds))
            {
                var packageIds = movedLinks
                    .Select(link => link.PackageUuid)
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

    private async Task ThrowIfExistingLinkWasRejectedAsync(string linkOrFile, CancellationToken ct)
    {
        var requestedLinks = linkOrFile
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLink)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedLinks.Count == 0)
        {
            return;
        }

        var rejectedLink = (await QueryAllLinkGrabberLinksAsync(ct))
            .FirstOrDefault(link => requestedLinks.Contains(NormalizeLink(link.Url)) && IsTerminalLinkFailure(link));
        if (rejectedLink == null)
        {
            return;
        }

        var reason = !string.IsNullOrWhiteSpace(rejectedLink.Name)
            ? rejectedLink.Name
            : rejectedLink.Status;
        reason = string.IsNullOrWhiteSpace(reason)
            ? "JDownloader rejected the link in LinkGrabber."
            : reason.Replace("!Unsupported", "! Unsupported", StringComparison.Ordinal);

        throw new DownloadRejectedException(reason);
    }

    private static string NormalizeLink(string link)
    {
        return link.Trim().TrimEnd('/');
    }

    private async Task<JsonElement?> InvokeApiAsync(string path, object[] parameters, CancellationToken ct)
    {
        var baseUrl = Config?.LocalApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new Exception("No JDownloader legacy API URL configured.");
        }

        var url = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        if (parameters.Length > 0)
        {
            // JDownloader's deprecated local API uses positional JSON values in the
            // query string. The My.JDownloader transport uses a POST envelope, but
            // sending that envelope directly to the local API returns HTTP 501.
            var query = string.Join(
                "&",
                parameters.Select(parameter => Uri.EscapeDataString(JsonSerializer.Serialize(parameter, JsonOptions))));
            url = $"{url}?{query}";
        }

        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out var error) &&
            error.ValueKind != JsonValueKind.Null &&
            error.ValueKind != JsonValueKind.Undefined)
        {
            throw new Exception($"JDownloader legacy API error: {error}");
        }

        return document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
    }

    private static LegacyJDownloaderPackage? FindPackage(IEnumerable<LegacyJDownloaderPackage> packages, string packageName)
    {
        return packages
            .OrderByDescending(package => package.Uuid)
            .FirstOrDefault(package => string.Equals(package.Name, packageName, StringComparison.OrdinalIgnoreCase));
    }

    private static long? TryReadResultId(JsonElement? data)
    {
        if (data is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numericId))
        {
            return numericId;
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("id", out var idProperty) &&
            idProperty.ValueKind == JsonValueKind.Number &&
            idProperty.TryGetInt64(out var objectId))
        {
            return objectId;
        }

        return null;
    }

    private static Dictionary<string, object> CreatePackageQuery(long[] packageIds)
    {
        var query = new Dictionary<string, object>
        {
            ["bytesLoaded"] = true,
            ["bytesTotal"] = true,
            ["childCount"] = true,
            ["enabled"] = true,
            ["eta"] = true,
            ["finished"] = true,
            ["name"] = true,
            ["running"] = true,
            ["saveTo"] = true,
            ["speed"] = true,
            ["status"] = true
        };

        if (packageIds.Length > 0)
        {
            query["packageUUIDs"] = packageIds;
        }

        return query;
    }

    private static void EnrichPackage(LegacyJDownloaderPackage package, LegacyJDownloaderLink[] links)
    {
        package.Links = links.Length > 0 ? links.Length : package.Links;
        package.LinksDone = links.Count(IsLinkFinished);

        var failedLink = links.FirstOrDefault(IsTerminalLinkFailure);
        if (failedLink != null && package.LinksDone == 0)
        {
            package.Finished = false;
            var failure = !string.IsNullOrWhiteSpace(failedLink.Name)
                ? failedLink.Name
                : failedLink.Status;
            package.Status = $"Failed: {failure}";
            return;
        }

        var bytesTotal = links.Sum(link => Math.Max(link.BytesTotal, link.Size));
        var bytesLoaded = links.Sum(link => link.BytesLoaded);

        if (package.BytesTotal <= 0 && bytesTotal > 0)
        {
            package.BytesTotal = bytesTotal;
        }

        if (package.BytesLoaded <= 0 && bytesLoaded > 0)
        {
            package.BytesLoaded = bytesLoaded;
        }

        package.Size = package.BytesTotal;

        if (IsPackageFinished(package))
        {
            package.Status = "Finished";
        }
    }

    private static bool IsPackageFinished(LegacyJDownloaderPackage package)
    {
        return package.Finished ||
               string.Equals(package.Status, "Finished", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(package.Status, "Finished(Mirror)", StringComparison.OrdinalIgnoreCase) ||
               (package.BytesTotal > 0 && package.BytesLoaded >= package.BytesTotal) ||
               (package.Links > 0 && package.LinksDone >= package.Links);
    }

    private static bool IsLinkFinished(LegacyJDownloaderLink link)
    {
        return link.Finished ||
               string.Equals(link.Status, "Finished", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(link.Status, "Finished(Mirror)", StringComparison.OrdinalIgnoreCase) ||
               (Math.Max(link.BytesTotal, link.Size) > 0 && link.BytesLoaded >= Math.Max(link.BytesTotal, link.Size));
    }

    private static bool IsTerminalLinkFailure(LegacyJDownloaderLink link)
    {
        return !IsLinkFinished(link) &&
               (string.Equals(link.Host, "linkcrawlerretry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(link.Status, "File not found", StringComparison.OrdinalIgnoreCase) ||
                link.Status.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LegacyJDownloaderPackage
    {
        public long Uuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SaveTo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long BytesLoaded { get; set; }
        public long BytesTotal { get; set; }
        public long Size { get; set; }
        public int Links { get; set; }
        public int LinksDone { get; set; }
        public bool Finished { get; set; }
    }

    private sealed class LegacyJDownloaderLink
    {
        public long Uuid { get; set; }
        public long PackageUuid { get; set; }
        public string Host { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public long BytesLoaded { get; set; }
        public long BytesTotal { get; set; }
        public long Size { get; set; }
        public bool Finished { get; set; }
    }

    private sealed class LegacyCrawlerJob
    {
        public int Broken { get; set; }
        public bool Checking { get; set; }
        public int Crawled { get; set; }
        public bool Crawling { get; set; }
        public int Filtered { get; set; }
        public int Unhandled { get; set; }
    }
}
