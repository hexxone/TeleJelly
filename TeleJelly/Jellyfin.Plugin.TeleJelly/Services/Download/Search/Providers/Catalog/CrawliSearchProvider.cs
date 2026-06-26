namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class CrawliSearchProvider : DisabledSearchProviderBase
{
    public CrawliSearchProvider() : base("crawli.net")
    {
        // Disabled scaffold: site challenge and dynamic anti-bot response prevents stable non-browser parsing right now.
    }
}