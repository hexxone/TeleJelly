using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class DdlWarezSearchProvider : ConfigurableStructuredSearchProvider
{
    public DdlWarezSearchProvider(ILogger<DdlWarezSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("ddl-warez.cc", "https://ddl-warez.cc/", SearchDiscoveryMode.WordPressRest, PostFetchMode.Html, logger, fetcher, "?s={0}", "video")
    {
    }
}