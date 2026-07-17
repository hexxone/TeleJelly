using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;

internal sealed class JDownloader2Service : IHostedDownloadService, IDeferredHostedDownloadService, IDisposable
{
    private const string CrawlerPrefix = "crawler:";
    private static JDownloader2Settings? Config => TeleJellyPlugin.Instance?.Configuration.DownloadManager.HostedServices.JDownloader2;

    private readonly IJDownloader2ServiceBackend _legacyJDownloader;
    private readonly ILogger _logger;
    private readonly IJDownloader2ServiceBackend _myJDownloader;

    public JDownloader2Service(ILogger<JDownloader2Service> logger)
    {
        _logger = logger;
        _myJDownloader = new MyJDownloader2Service(logger);
        _legacyJDownloader = new LegacyJDownloader2Service(logger);
    }

    public string ServiceName => "JDownloader2";

    public bool IsEnabled => Config?.Enabled ?? false;

    private IJDownloader2ServiceBackend ActiveBackend =>
        Config?.ConnectionMode == JDownloader2ConnectionMode.LocalOnly
            ? _legacyJDownloader
            : _myJDownloader;

    public bool CanHandle(string linkOrFile)
    {
        if (string.IsNullOrWhiteSpace(linkOrFile))
        {
            return false;
        }

        if (TryGetLocalDlcPath(linkOrFile, out var dlcPath))
        {
            return File.Exists(dlcPath);
        }

        return SplitLinks(linkOrFile)
            .All(link => Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == "http" || uri.Scheme == "https"));
    }

    public async Task<string> AddDownloadAsync(string linkOrFile, CancellationToken ct)
    {
        if (!TryGetLocalDlcPath(linkOrFile, out var dlcPath))
        {
            return await ActiveBackend.AddDownloadAsync(linkOrFile, ct);
        }

        var content = await File.ReadAllBytesAsync(dlcPath, ct);
        var crawlerJobId = await ActiveBackend.AddContainerAsync(content, "DLC", ct);
        DeleteTemporaryContainerFile(dlcPath);
        return CrawlerPrefix + crawlerJobId;
    }

    public bool IsDeferredDownload(string downloadId)
    {
        return downloadId.StartsWith(CrawlerPrefix, StringComparison.Ordinal);
    }

    public async Task<DeferredHostedDownloadProgress> ContinueDownloadAsync(string downloadId, CancellationToken ct)
    {
        if (!IsDeferredDownload(downloadId))
        {
            return new DeferredHostedDownloadProgress(true, false, "Ready", downloadId);
        }

        var crawlerJobId = downloadId[CrawlerPrefix.Length..];
        var progress = await ActiveBackend.GetContainerImportProgressAsync(crawlerJobId, ct);
        if (progress.IsFailed)
        {
            return new DeferredHostedDownloadProgress(false, true, progress.StatusText);
        }

        if (!progress.IsComplete)
        {
            return new DeferredHostedDownloadProgress(false, false, progress.StatusText);
        }

        var resolvedDownloadId = await ActiveBackend.CompleteContainerImportAsync(crawlerJobId, ct);
        return new DeferredHostedDownloadProgress(true, false, "All DLC links resolved and moved to Downloads", resolvedDownloadId);
    }

    public Task<object?> GetProgressAsync(string downloadId, CancellationToken ct)
    {
        return ActiveBackend.GetProgressAsync(downloadId, ct);
    }

    public Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct)
    {
        return ActiveBackend.GetDownloadDirectoryAsync(downloadId, ct);
    }

    public Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct)
    {
        return ActiveBackend.GetCompletedFilesAsync(downloadId, ct);
    }

    public Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct)
    {
        if (IsDeferredDownload(downloadId))
        {
            return ActiveBackend.CancelContainerImportAsync(downloadId[CrawlerPrefix.Length..], ct);
        }

        return ActiveBackend.RemoveDownloadAsync(downloadId, deleteFiles, ct);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        return ActiveBackend.TestConnectionAsync(ct);
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

    public void Dispose()
    {
        _myJDownloader.Dispose();
        _legacyJDownloader.Dispose();
    }

    private static IEnumerable<string> SplitLinks(string linkOrFile)
    {
        return linkOrFile
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryGetLocalDlcPath(string linkOrFile, out string path)
    {
        path = string.Empty;
        if (!Uri.TryCreate(linkOrFile, UriKind.Absolute, out var uri) ||
            !uri.IsFile ||
            !uri.LocalPath.EndsWith(".dlc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = uri.LocalPath;
        return true;
    }

    private static void DeleteTemporaryContainerFile(string path)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var filePath = Path.GetFullPath(path);
        if (filePath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
