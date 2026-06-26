using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal interface ISearchDocumentFetcher
{
    Task<string> GetStringAsync(Uri uri, CancellationToken ct);

    Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct);
}