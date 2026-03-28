# Search And Provider Extension

## Current search design

The search layer is centered around `ISearchProvider`:

```csharp
public interface ISearchProvider
{
    string Name { get; }
    Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct);
}
```

Important implications:

- providers receive both a human query and an optional IMDb ID,
- providers are free to prefer IMDb-first search when the site supports or benefits from it,
- the provider contract expects final `SearchResult` objects that the rest of the pipeline can act on.

`SearchOrchestrator` then:

- filters providers by `Search.EnabledServices`,
- invokes each provider,
- catches provider-local failures so one site does not break the entire search,
- scores results through `QualityRuleEngine`,
- returns the top matches.

## Current provider groups

### Structured providers

The preferred path is `ConfigurableStructuredSearchProvider` in `Services/Download/Search/Providers/StructuredSearchProviders.cs`.

It exists for providers where we can define:

- how discovery search works,
- how the actual post page must be fetched,
- how real download links and passwords are extracted.

Current structured providers:

- `funxd.site`
- `jjs.page`
- `hd-source.to`
- `ddl-warez.cc`
- `movieblog.to`
- `hdencode.org`

This path is what the recent recon and implementation work was built around.

### Legacy generic providers

`GenericHtmlSearchProviderBase` still exists for basic HTML search pages, but it is a weaker fallback.

Current examples:

- `filmfans.org`
- `serienfans.org`

These providers do not yet have the same site-aware crawler depth as the structured adapters.

### Disabled placeholders

Some providers are registered but intentionally disabled because their live behavior is not stable enough for deterministic server-side scraping right now.

Current examples:

- `crawli.net`
- `data-load.me`
- `nima4k.org`
- `disco-load.cc`
- `byte.to`

## Rules for adding a new provider

Start with the recon docs first:

- [../search-recon/10-wordpress-rest-providers.md](../search-recon/10-wordpress-rest-providers.md)
- [../search-recon/20-html-and-protected-providers.md](../search-recon/20-html-and-protected-providers.md)
- [../search-recon/30-custom-and-forum-providers.md](../search-recon/30-custom-and-forum-providers.md)
- [../search-recon/40-passwords-and-imdb-strategy.md](../search-recon/40-passwords-and-imdb-strategy.md)

Then choose the adapter shape based on the site:

1. Use `ConfigurableStructuredSearchProvider` when the site can be described by a small number of search and post-fetch modes.
2. Use `GenericHtmlSearchProviderBase` only when the site is genuinely simple and there is no better site-specific adapter yet.
3. Create a dedicated provider class when the site has custom APIs, forum flows, login/session requirements, anti-bot constraints, or protected intermediate forms.
4. Leave the provider disabled if the only path would be brittle scraping that is likely to break quickly.

## Implementation expectations

A new provider should follow these rules:

- Prefer IMDb-based search when it improves match reliability.
- Fall back to title query when IMDb search is not supported or returns nothing.
- Crawl through the actual content page, not just the search result listing.
- Return actionable links:
  torrent URL, magnet, file container, or direct hoster links.
- Capture passwords whenever the page exposes them.
- Preserve metadata that improves ranking:
  size, resolution, source, codec, HDR, languages, seeders, uploaded date.
- Shape the payload for the downloader that will receive it.
  For hosted downloads, multiple direct part links can be bundled together.

The key design principle is: do not stop at “found a post URL”. The provider should get as close as possible to the real downloadable payload.

## Rough steps to add a provider

1. Inspect the live site with local temporary `curl` or `wget` output and document the result under `docs/search-recon` if the site is new.
2. Implement the provider class under `Services/Download/Search/Providers/`.
3. Register it in `TeleJellyServiceRegistrator.cs` as an `ISearchProvider`.
4. Give it a stable `Name` string that matches what users place in `Search.EnabledServices`.
5. Verify that the returned `SearchResult` works with the intended backend:
   torrent service, hosted-link service, or both.
6. Confirm password propagation and extraction behavior when the source is archive-based.
7. Add or update tests where practical, especially orchestrator-level behavior.

## Current extension guidance

- Reuse `ConfigurableStructuredSearchProvider` unless there is a real reason not to.
- Treat IMDb support as a first-class input, not an afterthought.
- Expect site layouts to drift over time; document the observed mechanism, not just the final URL.
- If a provider requires authenticated forum scraping or heavy anti-bot bypassing, document that explicitly and keep it disabled until there is a maintainable approach.
