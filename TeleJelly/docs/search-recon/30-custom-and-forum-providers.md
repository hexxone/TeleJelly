# Custom And Forum Providers

These providers were probed during recon but were not yet converted into stable adapters.

## `filmfans.org`

Observed behavior:

- Homepage exposes a custom search input:
  - `id="searchInput"`
  - `id="searchbox"`
- No obvious regular server-rendered search route was found from HTML alone.
- The site loads a large custom script:
  - `/moviefans.js?...`
- The script is heavily obfuscated/minified.

Current assessment:

- Search appears to be client-side/custom-script driven.
- Needs dedicated reverse engineering of the JS request flow or a reproducible search XHR.

Implementation guidance:

- Do not route this through the generic WordPress provider.
- Future work should focus on:
  - browser-network style request reproduction
  - locating the exact autocomplete/result endpoint
  - determining whether search returns post IDs, raw HTML, or JSON

## `serienfans.org`

Observed behavior:

- Similar to `filmfans.org`.
- Homepage exposes:
  - `id="searchInput"`
  - `id="searchbox"`
- Loads a large custom script:
  - `/serienfans.js?...`
- Script is heavily obfuscated/minified.

Current assessment:

- Likely another custom client-driven search flow.
- Needs its own adapter, not a generic search-page scraper.

## `data-load.me`

Observed behavior:

- XenForo/forum-style search.
- Public search route shape is visible:
  - `/search/?keywords=<term>&c[title_only]=1&o=date`
- Result pages fetched during recon often showed no hits for the sampled terms.
- Public HTML clearly indicates XenForo quick-search/search-form structure.

Current assessment:

- Search route is known.
- Still unresolved:
  - consistent result parsing
  - guest visibility of thread download links
  - whether thread pages require login or extra interaction

Implementation guidance:

- Use `curl --globoff` when reproducing search URLs because of the `c[...]` query params.
- This should eventually get a XenForo-specific adapter.

## `disco-load.cc`

Observed behavior:

- Also XenForo/forum-style.
- Search route shape is visible:
  - `/search/?keywords=<term>&c[title_only]=1&o=date`
- Public samples fetched during recon returned `Keine Ergebnisse gefunden.` for tested terms.

Current assessment:

- Route shape is known.
- Still needs:
  - reproducible positive sample terms
  - result parser
  - thread-page inspection for download visibility

## `byte.to`

Observed behavior:

- Homepage search form exists, but only a light trace was collected.
- The site did not yet get enough probing to define a stable adapter.

Current assessment:

- Still unresolved.

## `nima4k.org`

Observed behavior:

- Not WordPress.
- Appears MyBB/forum-like.
- Search-related endpoints seen in HTML:
  - `search.php?action=getnew`
  - `search.php?action=getdaily`
  - a search form posting to `/search`
- The site also references `search.html?xrel_search_query=...` links in markup.

Current assessment:

- Search may be hybrid forum/custom index.
- Needs dedicated mapping before implementation.

## `crawli.net`

Observed behavior:

- Front page reachable, but no stable adapter work completed.
- Prior concern about anti-bot behavior is still valid.

Current assessment:

- Leave disabled until a deterministic guest-visible search flow is identified.
