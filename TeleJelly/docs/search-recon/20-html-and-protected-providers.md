# HTML And Protected Providers

These providers do not behave like clean REST-backed WordPress search even when they are WordPress underneath.

## `hd-source.to`

Observed behavior:

- REST search endpoint exists but currently returns:
  - `{"code":"nfw_rest_api_access_restricted","message":"Forbidden access","data":{"status":404}}`
- Plain themed search still works:
  - `GET https://hd-source.to/?s=<term>`

Important page structure:

- Search HTML contains direct post URLs.
- The site also ships Ajax Search Pro assets and `admin-ajax.php`, but plain result-page crawling is already sufficient.
- Individual post pages expose:
  - one or more Filecrypt containers
  - visible password text
  - release metadata

Observed post details:

- Post page contained multiple Filecrypt containers.
- Password was visible in HTML as `Passwort:`.
- Affiliate helper URLs like `/out/af.php?v=rapidgator` also appear, but Filecrypt is the better canonical target when present.

Implementation guidance:

- Use HTML search, not REST.
- Crawl search result anchors and fetch post HTML.
- Prefer Filecrypt containers.
- Extract password from visible HTML.

## `hdencode.org`

Observed behavior:

- REST search exists but returned a Cloudflare/anti-automation block in at least one direct call.
- The normal HTML search page works:
  - `GET https://hdencode.org/?s=<term>`

Important page structure:

- Search result page contains regular post links and pagination.
- Individual post pages do not expose links immediately.
- Download links are hidden behind a content-protector form on the same page.

Observed unlock flow:

- The locked page contains hidden inputs:
  - `content-protector-captcha`
  - `content-protector-token`
  - `content-protector-ident`
  - `chax-response`
  - `content-protector-submit`
- A simple server-side `POST` of those fields back to the post URL successfully unlocks the links.
- The unlocked response then contains direct host links, observed for:
  - `rapidgator.net`
  - `nitroflare.com`

Important constraint:

- This provider may return many direct part links instead of one container.
- Those should be grouped into a newline-separated payload when the downstream hosted downloader accepts multiple links in one request.

Implementation guidance:

- Use HTML search, not REST.
- Fetch post HTML.
- Detect and submit the content-protector form.
- Parse the unlocked HTML for hoster part links.
- Preserve any future password if it appears, but the sampled page did not show one.

## Generic fallback lesson

The old generic provider behavior was too weak because it:

- searched one URL shape only
- extracted any link from the search page rather than the post page
- ignored site-specific unlock flows
- ignored passwords
- ignored multi-link host payloads

For providers in this file, the reliable unit of work is:

1. discover post URL
2. fetch post page
3. unlock if needed
4. extract actionable download target(s)
5. extract password and metadata
