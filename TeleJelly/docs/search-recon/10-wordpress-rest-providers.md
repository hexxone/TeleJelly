# WordPress REST Providers

These providers expose enough WordPress REST surface to use API-first search and/or post retrieval.

## Common Pattern

Search:

- `GET /wp-json/wp/v2/search?search=<term>&per_page=<n>`

Post details:

- Usually available via the `_links.self[0].href` URL in the search payload.
- The post payload often contains `title.rendered`, `content.rendered`, and `date_gmt`.

Why this matters:

- REST search is more stable than scraping the themed search result page.
- The post JSON frequently includes the exact Filecrypt/hoster links and metadata we need.

## `funxd.site`

Observed behavior:

- Homepage and search are WordPress-backed.
- `GET https://funxd.site/wp-json/wp/v2/search?search=matrix&per_page=10` returns post hits directly.
- `GET https://funxd.site/wp-json/wp/v2/posts/<id>` returns the actual post payload.

Important page structure:

- `content.rendered` contains:
  - a Filecrypt container link in the release line
  - inline metadata such as audio, language, format, and size
- Example pattern seen:
  - `Release: <a href="https://www.filecrypt.cc/Container/...">...`
  - `Sprache: GER / ENG`
  - `Size: 2717MB`

Search notes:

- Title search works.
- IMDb ID search via `?s=tt0133093` is not obviously reliable from the HTML alone, so it should be treated as a first attempt, not the only strategy.

Implementation guidance:

- Prefer REST search.
- Prefer REST post detail over themed HTML.
- Extract:
  - Filecrypt container
  - size
  - language
  - codec / source / resolution when present in the release text

## `jjs.page`

Observed behavior:

- Search page is WordPress-based and behind Cloudflare, but direct GETs are still accessible.
- `GET https://jjs.page/wp-json/wp/v2/search?search=matrix&per_page=10` works.
- `GET https://jjs.page/wp-json/wp/v2/posts/<id>` works.

Important page structure:

- `content.rendered` contains strongly structured blocks:
  - `DDLContent` with Filecrypt container links
  - `ReleaseGeneral` with tone/subtitle/video metadata
  - `ReleaseDownload` with part size, part count, total size
- Example pattern seen:
  - `Direct Download Links | DDL-Links`
  - `href="https://filecrypt.cc/Container/..."`
  - `Partgröße`, `Parts`, `Gesamtgröße`

Search notes:

- Title search works well.
- IMDb ID HTML search like `?s=tt10838180` resolves to the normal search route, but reliability still needs to be judged per title.

Implementation guidance:

- REST search is the clean path.
- REST post detail is better than themed HTML.
- Extract:
  - Filecrypt links from `DDLContent`
  - subtitle/audio/video metadata
  - total size

## `ddl-warez.cc`

Observed behavior:

- REST search works:
  - `GET /wp-json/wp/v2/search?search=<term>&per_page=10`
- Results include custom subtypes such as `video` and `game`.
- Relevant media hits use the custom detail endpoint from `_links.self`, for example:
  - `/wp-json/wp/v2/video/<id>`

Important page structure:

- The custom post payload stores data inside `content.rendered` as pseudo-JSON text.
- Observed fields:
  - `releasetitel`
  - `media_nfo`
  - `size`
  - `imdb_id`
  - `m0_hoster`
  - `m1_hoster`
  - `password`

HTML page behavior:

- The public HTML post page also exposes:
  - a visible password block
  - mirror buttons such as `Download Mirror 1`
  - affiliate routes like `/azn/af.php?...`
- The actual final host links are not directly embedded in the initial HTML as plain download URLs.

Implementation guidance:

- Use REST search.
- For now, HTML page crawling is the safest post path because it exposes password and mirror buttons cleanly.
- Filter search results to media-relevant subtypes such as `video`.
- Treat `/azn/af.php?...` as the actionable download target unless a future adapter resolves it to final Filecrypt/host links.

## `movieblog.to`

Observed behavior:

- REST search works:
  - `GET /wp-json/wp/v2/search?search=<term>&per_page=10`
- REST post detail works:
  - `GET /wp-json/wp/v2/posts/<id>`

Important page structure:

- `content.rendered` contains:
  - NFO / media block
  - one or more Filecrypt links
  - a visible password line
- Example pattern seen:
  - `Download: <a href="https://www.filecrypt.cc/Container/...">Rapidgator.net</a>`
  - `Mirror #1: <a href="https://www.filecrypt.cc/Container/...">ddownload.com</a>`
  - `Passwort: movieblog.to`

Search notes:

- HTML search by IMDb ID such as `?s=tt1446714` routes normally, but no evidence yet that IMDb-only search is strictly better than title search.

Implementation guidance:

- Prefer REST search and REST post detail.
- Extract:
  - all Filecrypt containers
  - password
  - size
  - codec/HDR/resolution from the NFO/release text
