using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal interface ISearchDocumentFetcher
{
    Task<SearchDocumentResponse> GetResponseAsync(Uri uri, CancellationToken ct);

    Task<string> GetStringAsync(Uri uri, CancellationToken ct);

    Task<byte[]> GetBytesAsync(Uri uri, CancellationToken ct);

    Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct);
}

internal sealed record SearchDocumentResponse(HttpStatusCode StatusCode, Uri FinalUri, string Content);
