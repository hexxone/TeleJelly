namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class Nima4kSearchProvider : DisabledSearchProviderBase
{
    public Nima4kSearchProvider() : base("nima4k.org")
    {
        // Disabled scaffold: anti-automation protections interfere with deterministic scraping.
    }
}