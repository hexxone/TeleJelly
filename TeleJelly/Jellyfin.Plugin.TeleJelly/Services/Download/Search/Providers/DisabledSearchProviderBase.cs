using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal abstract class DisabledSearchProviderBase : ISearchProvider
{
    protected DisabledSearchProviderBase(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct)
    {
        return Task.FromResult<IEnumerable<SearchResult>>([]);
    }
}
