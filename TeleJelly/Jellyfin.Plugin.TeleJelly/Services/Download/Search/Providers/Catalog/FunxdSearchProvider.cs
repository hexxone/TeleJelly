using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class FunxdSearchProvider : ConfigurableStructuredSearchProvider
{
    public FunxdSearchProvider(ILogger<FunxdSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("funxd.site", "https://funxd.site/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger, fetcher)
    {
    }
}
