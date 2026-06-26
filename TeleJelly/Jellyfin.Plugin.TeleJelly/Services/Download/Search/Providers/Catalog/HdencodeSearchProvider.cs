using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class HdencodeSearchProvider : ConfigurableStructuredSearchProvider
{
    public HdencodeSearchProvider(ILogger<HdencodeSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("hdencode.org", "https://hdencode.org/", SearchDiscoveryMode.WordPressHtml, PostFetchMode.HdEncodeProtectedHtml, logger, fetcher)
    {
    }
}