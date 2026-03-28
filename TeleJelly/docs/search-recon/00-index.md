# Search Provider Recon Index

This folder captures live reconnaissance done with `curl`/`wget` against the current upstream provider sites.

Purpose:

- Avoid re-learning provider-specific search behavior on the next pass.
- Record which sites expose stable APIs versus HTML-only search.
- Record where download links and passwords are actually embedded.
- Keep findings grouped by mechanism instead of mixing all providers into one file.

Files:

- [10-wordpress-rest-providers.md](10-wordpress-rest-providers.md)
  WordPress sites where REST search and/or REST post payloads are usable.
- [20-html-and-protected-providers.md](20-html-and-protected-providers.md)
  Sites that require HTML crawling, protected POST unlocks, or fallback parsing.
- [30-custom-and-forum-providers.md](30-custom-and-forum-providers.md)
  XenForo/custom-script/forum-backed sites that still need dedicated adapters.
- [40-passwords-and-imdb-strategy.md](40-passwords-and-imdb-strategy.md)
  Cross-cutting rules for passwords, IMDb-first lookups, and download payload shaping.
