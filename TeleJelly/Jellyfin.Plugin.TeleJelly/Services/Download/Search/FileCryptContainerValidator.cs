using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public enum DownloadLinkValidationStatus
{
    NotApplicable,
    Reachable,
    Broken,
    Unknown
}

public interface IDownloadLinkValidator
{
    bool CanValidate(string link);

    Task<DownloadLinkValidationStatus> ValidateAsync(string link, CancellationToken ct);
}

internal sealed class FileCryptContainerValidator : IDownloadLinkValidator
{
    private readonly ISearchDocumentFetcher _fetcher;
    private readonly ILogger<FileCryptContainerValidator> _logger;

    public FileCryptContainerValidator(ILogger<FileCryptContainerValidator> logger)
        : this(logger, new HttpClientSearchDocumentFetcher())
    {
    }

    internal FileCryptContainerValidator(ILogger<FileCryptContainerValidator> logger, ISearchDocumentFetcher fetcher)
    {
        _logger = logger;
        _fetcher = fetcher;
    }

    public bool CanValidate(string link)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
               uri.Host.Equals("filecrypt.cc", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith("/Container/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DownloadLinkValidationStatus> ValidateAsync(string link, CancellationToken ct)
    {
        if (!CanValidate(link))
        {
            return DownloadLinkValidationStatus.NotApplicable;
        }

        try
        {
            var response = await _fetcher.GetResponseAsync(new Uri(link, UriKind.Absolute), ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ||
                response.FinalUri.AbsolutePath.Equals("/404.html", StringComparison.OrdinalIgnoreCase) ||
                LooksLikeFileCryptNotFoundPage(response.Content))
            {
                _logger.LogInformation("Rejected broken FileCrypt container {Link}; final URL was {FinalUrl}", link, response.FinalUri);
                return DownloadLinkValidationStatus.Broken;
            }

            if ((int)response.StatusCode is >= 200 and <= 299 &&
                response.FinalUri.AbsolutePath.StartsWith("/Container/", StringComparison.OrdinalIgnoreCase))
            {
                return DownloadLinkValidationStatus.Reachable;
            }

            return DownloadLinkValidationStatus.Unknown;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // CAPTCHA/challenge pages and temporary network failures must not turn a
            // potentially valid result into a false negative.
            _logger.LogWarning(ex, "Could not preflight FileCrypt container {Link}", link);
            return DownloadLinkValidationStatus.Unknown;
        }
    }

    private static bool LooksLikeFileCryptNotFoundPage(string content)
    {
        return content.Contains("Leider konnten wir nicht finden", StringComparison.OrdinalIgnoreCase) ||
               content.Contains(">Nicht gefunden<", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<title>404", StringComparison.OrdinalIgnoreCase);
    }
}
