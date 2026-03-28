using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal sealed class FunxdSearchProvider : ConfigurableStructuredSearchProvider
{
    public FunxdSearchProvider(ILogger<FunxdSearchProvider> logger)
        : base("funxd.site", "https://funxd.site/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger)
    {
    }
}

internal sealed class JjsSearchProvider : ConfigurableStructuredSearchProvider
{
    public JjsSearchProvider(ILogger<JjsSearchProvider> logger)
        : base("jjs.page", "https://jjs.page/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger)
    {
    }
}

internal sealed class HdSourceSearchProvider : ConfigurableStructuredSearchProvider
{
    public HdSourceSearchProvider(ILogger<HdSourceSearchProvider> logger)
        : base("hd-source.to", "https://hd-source.to/", SearchDiscoveryMode.WordPressHtml, PostFetchMode.Html, logger)
    {
    }
}

internal sealed class FilmfansSearchProvider : GenericHtmlSearchProviderBase
{
    public FilmfansSearchProvider(ILogger<FilmfansSearchProvider> logger)
        : base("filmfans.org", "https://filmfans.org/?s={0}", logger)
    {
    }
}

internal sealed class SerienfansSearchProvider : GenericHtmlSearchProviderBase
{
    public SerienfansSearchProvider(ILogger<SerienfansSearchProvider> logger)
        : base("serienfans.org", "https://serienfans.org/?s={0}", logger)
    {
    }
}

internal sealed class CrawliSearchProvider : DisabledSearchProviderBase
{
    public CrawliSearchProvider() : base("crawli.net")
    {
        // Disabled scaffold: site challenge and dynamic anti-bot response prevents stable non-browser parsing right now.
    }
}

internal sealed class DataLoadSearchProvider : DisabledSearchProviderBase
{
    public DataLoadSearchProvider() : base("data-load.me")
    {
        // Disabled scaffold: endpoint shape is inconsistent and requires authenticated/session-aware scraping.
    }
}

internal sealed class DdlWarezSearchProvider : ConfigurableStructuredSearchProvider
{
    public DdlWarezSearchProvider(ILogger<DdlWarezSearchProvider> logger)
        : base("ddl-warez.cc", "https://ddl-warez.cc/", SearchDiscoveryMode.WordPressRest, PostFetchMode.Html, logger, "?s={0}", "video")
    {
    }
}

internal sealed class Nima4kSearchProvider : DisabledSearchProviderBase
{
    public Nima4kSearchProvider() : base("nima4k.org")
    {
        // Disabled scaffold: anti-automation protections interfere with deterministic scraping.
    }
}

internal sealed class MovieblogSearchProvider : ConfigurableStructuredSearchProvider
{
    public MovieblogSearchProvider(ILogger<MovieblogSearchProvider> logger)
        : base("movieblog.to", "https://movieblog.to/", SearchDiscoveryMode.WordPressRest, PostFetchMode.WordPressJson, logger)
    {
    }
}

internal sealed class DiscoLoadSearchProvider : DisabledSearchProviderBase
{
    public DiscoLoadSearchProvider() : base("disco-load.cc")
    {
        // Disabled scaffold: unstable markup and route changes make parsing brittle without dedicated adapter work.
    }
}

internal sealed class HdencodeSearchProvider : ConfigurableStructuredSearchProvider
{
    public HdencodeSearchProvider(ILogger<HdencodeSearchProvider> logger)
        : base("hdencode.org", "https://hdencode.org/", SearchDiscoveryMode.WordPressHtml, PostFetchMode.HdEncodeProtectedHtml, logger)
    {
    }
}

internal sealed class ByteToSearchProvider : DisabledSearchProviderBase
{
    public ByteToSearchProvider() : base("byte.to")
    {
        // Disabled scaffold: current site behavior blocks dependable server-side scraping.
    }
}
