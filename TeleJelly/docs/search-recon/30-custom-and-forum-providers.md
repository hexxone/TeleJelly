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

July 2026 recheck:

- The public XenForo search form is reachable and exposes a per-response `_xfToken`.
- A plain GET with search query parameters only renders the search form; the real search is a token/session-aware POST to `/search/search`.

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

The July 2026 recheck confirmed that the site is reachable without a browser challenge, but its public search still requires the XenForo form POST flow and the sampled `Airplane` query produced no positive result.

Current assessment:

- Route shape is known.
- Still needs:
  - reproducible positive sample terms
  - result parser
  - thread-page inspection for download visibility

## `byte.to`

Observed behavior:

- Homepage search is a stable GET form using `/?q=<term>`.
- A July 2026 `Airplane` search returned many broad matches, including unrelated magazines and the 2025 film.
- The exact localized 1980 title returned no result.

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

The July 2026 recheck confirmed that `POST /search` with a `search` field works without a CAPTCHA, but returned no result for `Airplane`.

Current assessment:

- Search may be hybrid forum/custom index.
- Needs dedicated mapping before implementation.

## `crawli.net`

Observed behavior:

- The public search form is deterministic: `POST /all/` with `opt=1`, `cat=all`, and `key=<term>`.
- Responses are wrapped in a base64 string that the normal page decodes with JavaScript.
- Decoding exposes result titles and `/go/?/<id>/` hop URLs.
- The hop page does not expose a final downloader payload; it redirects in JavaScript to a third-party source post.

Current assessment:

- Leave disabled until indexed source posts can be turned into actionable containers, magnets, torrents, or direct host links without provider-specific guesswork.
