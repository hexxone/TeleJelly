# Search And Provider Extension

## Current search design

The search layer is centered around `ISearchProvider`. Each provider knows how to ask one website or service for possible downloads.

```csharp
public interface ISearchProvider
{
    string Name { get; }
    Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct);
}
```

Important behavior:

- providers receive both a human query and an optional IMDb ID,
- providers are free to prefer IMDb-first search when the site supports or benefits from it,
- the provider contract expects final `SearchResult` objects that the rest of the pipeline can act on.

`SearchOrchestrator` then:

- filters providers by `Search.EnabledServices`,
- invokes each provider in parallel,
- searches the canonical title and a bounded set of TMDB localized/alternative titles,
- catches provider-local failures so one site does not break the entire search,
- rejects results whose year/season conflicts or whose title matches neither the canonical nor an alternative title,
- scores results through `QualityRuleEngine`,
- returns the top matches.

TMDB localized and alternative titles are resolved once with the IMDb metadata and persisted on the managed download. The German `de-DE` localized title is included explicitly because translated release titles are not guaranteed to appear in TMDB's alternative-title endpoint. German, Austrian, and Swiss aliases are searched first because the configured providers primarily publish German releases. Search fan-out is capped per provider, while the full persisted alias set remains available for result validation.

Title validation deliberately runs before quality scoring. Normalization ignores punctuation, separators, case, and diacritics, and accepts either a complete normalized title phrase or strong multi-token overlap. A matching year by itself is never enough. This prevents broad WordPress searches such as `Airplane 1980` from admitting unrelated 1980 releases while still accepting localized titles such as `Die unglaubliche Reise in einem verrückten Flugzeug`.

## Current provider groups

### Structured providers

The preferred path is `ConfigurableStructuredSearchProvider` in `Services/Download/Search/Providers/ConfigurableStructuredSearchProvider.cs`.

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

These sites were rechecked in July 2026. Their public pages are reachable, but none currently provides a complete, deterministic path from a guest search to an actionable download payload that fits `ISearchProvider`:

- `crawli.net` exposes a base64-wrapped search index and JavaScript hop pages, but the hop resolves only to another source post rather than a download payload;
- `byte.to` has a stable `?q=` search form, but exact localized-title coverage and detail-page extraction are not reliable enough yet;
- `nima4k.org` has a working POST search form but no result for the reproduction title;
- `data-load.me` and `disco-load.cc` expose XenForo search forms that require session/token-aware POST handling and positive guest-visible samples before an adapter can be considered complete.

They therefore remain disabled rather than returning source pages that a downloader cannot use.

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

## Expectations for a useful provider

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

The key design principle is: do not stop at "found a post URL". The provider should get as close as possible to the real downloadable payload, because the rest of the workflow needs something it can send to Transmission, qBittorrent, JDownloader2, or pyLoad.

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
