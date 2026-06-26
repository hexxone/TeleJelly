using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class HdSourceSearchProvider : ConfigurableStructuredSearchProvider
{
    public HdSourceSearchProvider(ILogger<HdSourceSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("hd-source.to", "https://hd-source.to/", SearchDiscoveryMode.WordPressHtml, PostFetchMode.Html, logger, fetcher)
    {
    }
}