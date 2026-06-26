# Download Manager Status And Priorities

This page replaces the old checklist and implementation-plan files. It describes what is already working, what still needs attention, and how important each remaining item is.

The download manager is no longer just a plan. It now has a working foundation: Telegram workflows, downloader integrations, search providers, service health checks, archive extraction, file organization, a basic admin view, logs, and automated tests.

It should still be treated as a feature in active development. The main remaining work is making setup easier, making recovery more predictable, and giving admins better controls without asking them to edit raw JSON.

## Current State

Implemented:

- Telegram can start a manual or automatic download workflow from an IMDb ID.
- Users can choose a Jellyfin library, pick search results, confirm paths, and start the download from Telegram.
- Transmission, qBittorrent, JDownloader2, and pyLoad integrations exist.
- The plugin checks downloader health and avoids services that are offline.
- Search results can be collected from multiple providers and ranked with quality rules.
- Provider passwords can be carried into archive extraction.
- Archive extraction supports passwords, multipart archive detection, free-space checks, optional archive cleanup, and limited recursive extraction.
- Finished files can be analyzed, moved into the selected Jellyfin library path, and followed by a library scan.
- The admin config page shows download status, service health, recent download-manager logs, and basic actions such as cancel, retry, remove, and remove with file cleanup.
- A local Docker Compose stack and test guide exist.
- Unit and component tests cover search ranking, providers, archive extraction, path templates, health checks, workflow policies, logging, and Telegram presentation helpers.

Not fully finished:

- The download-manager settings UI is still mostly a raw JSON editor.
- Restart recovery restores records, but it does not yet have a complete state-by-state resume strategy for every interrupted workflow step.
- Automatic best-result selection is not implemented; automatic downloads still require user confirmation.
- Downloader selection is priority-based fallback, not load balancing.
- JDownloader2 remains harder to test locally because the implementation depends on My.JDownloader-style behavior.
- The local stack is useful for development, but it is not a production deployment recipe.

## Remaining Work By Priority

### High Priority

These items affect correctness, data safety, or whether a normal user can complete setup.

1. Build a real download-manager settings UI.
   Today, admins can see status and edit raw JSON, but normal setup still requires knowing the internal config shape. Add form controls for enabling the feature, downloader settings, extraction settings, provider selection, quality profiles, library paths, and cleanup rules.

2. Finish restart recovery rules.
   The plugin reloads saved downloads after a restart, but each state needs explicit recovery behavior. A restart during downloading, extraction, analysis, or organization should either continue safely or explain exactly why user action is needed.

3. Add service connection tests from the UI.
   The health monitor already tests services in the background. Admins also need manual "test connection" buttons while configuring Transmission, qBittorrent, JDownloader2, and pyLoad.

4. Add end-to-end smoke tests with the local stack.
   Unit tests are much stronger now, but the real confidence gap is still full workflow testing: search or submit link, download, extract, organize, and verify that Jellyfin can see the final file.

5. Improve HTTPS and reverse-proxy troubleshooting.
   Login failures behind proxies are a common support case. Keep the README and config-page help focused on one clear checklist: public HTTPS URL, BotFather domain, configured base URL, forwarded host, and scheme handling.

### Medium Priority

These items improve reliability, operations, or admin efficiency, but the current feature can still operate without them.

1. Add optional automatic best-result selection.
   The search and scoring pieces already exist. Add a configurable mode that can auto-start the highest-confidence result, while keeping confirmation for ambiguous results.

2. Add optional load balancing.
   Current behavior prefers Transmission before qBittorrent and JDownloader2 before pyLoad. Optional balancing should only apply when services are equally healthy and should consider active job counts and recent failures.

3. Harden JDownloader2 package discovery and retry behavior.
   Package polling is better than a fixed delay now, but failed link-grabber retries and local/direct testing behavior still need more coverage.

4. Expand the admin dashboard.
   Add details, better filtering, sorting, bulk actions, active counts per service, cleanup countdowns, and clearer status messages.

5. Improve provider maintenance documentation.
   Search-provider behavior can change when websites change. Keep the recon docs short, current, and focused on what each provider can actually return.

6. Split non-download-manager backlog items.
   Login error messaging, notification checkbox changes, group layout cleanup, and build-helper improvements are valid work, but they should not be mixed into the download-manager release criteria.

### Low Priority

These items are useful polish or future expansion.

1. Add more provider adapters.
   Add more sources only after the existing provider set is stable and testable.

2. Add integrations such as Jackett, Prowlarr, or Seerr.
   These may be valuable later, but they change the product shape and should be considered separately.

3. Improve build helper packaging.
   `JellyfinPluginHelper` still has cleanup opportunities around deriving the target ABI and packaging helper logic, but it does not block download-manager usage.

4. Add richer diagrams and architecture notes.
   Useful for maintainers, but lower priority than setup, recovery, and test coverage.

## What Was Removed

The following old planning files were deleted because they duplicated this page or described work that has already been implemented:

- the old root-level checklist,
- the old implementation guide,
- the old plugin-level checklist,
- the local agent prompt.

Keep future planning in this document or in issue tracker tickets. Avoid adding new broad checklist files that mix completed work, feature plans, and release blockers.

## Release Readiness Checklist

Before treating the download manager as release-ready, verify:

- A non-technical admin can configure the feature without editing JSON.
- Manual and automatic workflows both finish from IMDb ID to organized Jellyfin files.
- Restarting Jellyfin during each major state behaves predictably.
- All enabled downloaders use shared staging paths that the plugin can read.
- Archive extraction handles protected and multipart samples in tests.
- The admin page can cancel, retry, remove, and clean up downloads safely.
- Local stack smoke tests pass.
- README and user docs match the behavior that is actually implemented.
