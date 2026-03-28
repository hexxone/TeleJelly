# Passwords And IMDb Strategy

This file captures the cross-provider rules that were learned during live probing.

## Password Extraction Rules

Passwords are worth preserving because the download pipeline already retries archive extraction with a password list.

Observed sources:

- `funxd.site`
  no explicit password seen in sampled post
- `jjs.page`
  no password seen in sampled post
- `ddl-warez.cc`
  password present in both REST-like content payload and HTML page
- `movieblog.to`
  password visible in post HTML / REST-rendered content
- `hd-source.to`
  password visible in post HTML
- `hdencode.org`
  no password seen in sampled unlocked post, but this should still be checked on future pages

Recommended extraction order:

1. explicit `Passwort:` / `Password:` label in HTML text
2. structured field in post payload
3. only if necessary, adjacent label/value parsing around download sections

Pipeline implication:

- The selected `SearchResult` should carry an optional password.
- The chosen result should persist that password onto the managed download.
- Extraction should try:
  - configured passwords
  - source password from the selected provider result

## IMDb-First Search Strategy

The user suggestion is correct: some providers may be more reliable with IMDb ID than with title/year text.

Current rule from recon:

- Always allow a provider to try both:
  - IMDb ID first when available
  - human-readable query second
- Do not assume IMDb-only works everywhere.

What was observed:

- `funxd.site`
  accepts `?s=tt0133093`, but no strong evidence yet that IMDb-only is enough
- `jjs.page`
  accepts `?s=tt10838180`, but title search still clearly works
- `ddl-warez.cc`
  post payloads explicitly contain `imdb_id`, which makes IMDb-aware matching valuable even if search is still term-based
- `movieblog.to`
  search route accepts IMDb ID in the `s` query, but title search remains the confirmed path

Recommended provider behavior:

1. if IMDb ID is available, try it first
2. if that yields no actionable page candidates, fall back to title/year query
3. if both yield candidates, deduplicate by post URL and by final download target

## Multi-Link Hosted Payloads

Not every provider returns one canonical URL.

Observed cases:

- `filecrypt.cc` container
  single canonical link
- `ddl-warez.cc`
  mirror buttons / affiliate routes rather than direct final host links
- `hdencode.org`
  many direct part links after unlock

Recommended shaping:

- Prefer a Filecrypt container when available.
- If only direct host links exist and there are many parts, group them into one newline-separated payload for the hosted downloader.
- Hosted downloader `CanHandle` checks and package creation should accept newline-separated HTTP(S) links.

## Reproduction Commands

Useful patterns from recon:

```bash
# WordPress REST search
curl -L 'https://example.org/wp-json/wp/v2/search?search=matrix&per_page=10'

# WordPress post detail from search result _links.self
curl -L 'https://example.org/wp-json/wp/v2/posts/12345'

# Search route with array-like forum query params
curl --globoff -L 'https://forum.example/search/?keywords=matrix&c[title_only]=1&o=date'

# Save samples for later parsing
curl -L 'https://example.org/?s=matrix' -o /tmp/telejelly-search-recon/example-search.html

# HDEncode content-protector unlock pattern
curl -L -X POST 'https://hdencode.org/post-slug/#unlocked' \
  --data 'content-protector-captcha=1&content-protector-token=...&content-protector-ident=...&chax-response=...&content-protector-submit=Access+the+links'
```
