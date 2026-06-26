using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class MovieblogSearchProvider : ConfigurableStructuredSearchProvider
{
    public MovieblogSearchProvider(ILogger<MovieblogSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("movieblog.to", "https://movieblog.to/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger, fetcher)
    {
    }
}