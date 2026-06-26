using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal sealed class HttpClientSearchDocumentFetcher : ISearchDocumentFetcher
{
    private static readonly HttpClient HttpClient = new();

    public Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        return HttpClient.GetStringAsync(uri, ct);
    }

    public async Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(formValues);
        using var response = await HttpClient.PostAsync(uri, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
