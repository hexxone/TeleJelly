using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class JjsSearchProvider : ConfigurableStructuredSearchProvider
{
    public JjsSearchProvider(ILogger<JjsSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("jjs.page", "https://jjs.page/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger, fetcher)
    {
    }
}