using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class SerienfansSearchProvider : GenericHtmlSearchProviderBase
{
    public SerienfansSearchProvider(ILogger<SerienfansSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("serienfans.org", "https://serienfans.org/?s={0}", logger, fetcher)
    {
    }
}