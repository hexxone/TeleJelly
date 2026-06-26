namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class DataLoadSearchProvider : DisabledSearchProviderBase
{
    public DataLoadSearchProvider() : base("data-load.me")
    {
        // Disabled scaffold: endpoint shape is inconsistent and requires authenticated/session-aware scraping.
    }
}