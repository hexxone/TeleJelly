using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class FilmfansSearchProvider : GenericHtmlSearchProviderBase
{
    public FilmfansSearchProvider(ILogger<FilmfansSearchProvider> logger, ISearchDocumentFetcher? fetcher = null)
        : base("filmfans.org", "https://filmfans.org/?s={0}", logger, fetcher)
    {
    }
}