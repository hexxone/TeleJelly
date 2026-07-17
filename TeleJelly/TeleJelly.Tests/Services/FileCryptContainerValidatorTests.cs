using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Component")]
public class FileCryptContainerValidatorTests
{
    [Test]
    public async Task ValidateAsync_RejectsRedirectToFileCrypt404Page()
    {
        var fetcher = new ResponseFetcher(new SearchDocumentResponse(
            HttpStatusCode.OK,
            new Uri("https://filecrypt.cc/404.html"),
            "<h1>Nicht gefunden</h1>"));
        var validator = new FileCryptContainerValidator(NullLogger<FileCryptContainerValidator>.Instance, fetcher);

        var result = await validator.ValidateAsync(
            "https://filecrypt.cc/Container/2F0911C2FF.html",
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(DownloadLinkValidationStatus.Broken));
    }

    [Test]
    public async Task ValidateAsync_KeepsChallengeOrReachableContainer()
    {
        var uri = new Uri("https://filecrypt.cc/Container/ABC.html");
        var fetcher = new ResponseFetcher(new SearchDocumentResponse(HttpStatusCode.OK, uri, "powcaptcha.com"));
        var validator = new FileCryptContainerValidator(NullLogger<FileCryptContainerValidator>.Instance, fetcher);

        var result = await validator.ValidateAsync(uri.ToString(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(DownloadLinkValidationStatus.Reachable));
    }

    private sealed class ResponseFetcher(SearchDocumentResponse response) : ISearchDocumentFetcher
    {
        public Task<SearchDocumentResponse> GetResponseAsync(Uri uri, CancellationToken ct) => Task.FromResult(response);

        public Task<string> GetStringAsync(Uri uri, CancellationToken ct) => Task.FromResult(response.Content);

        public Task<byte[]> GetBytesAsync(Uri uri, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());

        public Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }
}
