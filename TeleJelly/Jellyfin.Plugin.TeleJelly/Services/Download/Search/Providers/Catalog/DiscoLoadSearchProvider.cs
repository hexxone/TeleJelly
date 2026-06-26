namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;

internal sealed class DiscoLoadSearchProvider : DisabledSearchProviderBase
{
    public DiscoLoadSearchProvider() : base("disco-load.cc")
    {
        // Disabled scaffold: unstable markup and route changes make parsing brittle without dedicated adapter work.
    }
}