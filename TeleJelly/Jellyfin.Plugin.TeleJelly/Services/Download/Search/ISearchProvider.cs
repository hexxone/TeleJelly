using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public interface ISearchProvider
{
    string Name { get; }
    Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct);
}
