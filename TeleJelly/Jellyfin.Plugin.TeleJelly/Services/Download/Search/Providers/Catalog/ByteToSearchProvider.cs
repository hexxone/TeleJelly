namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class ByteToSearchProvider : DisabledSearchProviderBase
{
    public ByteToSearchProvider() : base("byte.to")
    {
        // Disabled scaffold: current site behavior blocks dependable server-side scraping.
    }
}