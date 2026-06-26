# Download Manager Overview And Workflow

## What the feature does

The download manager turns a Telegram command into a guided download workflow:

1. A user starts an automated download with `/autodownload <imdb_id>`.
2. The plugin resolves metadata from the IMDb/TMDb side.
3. The user selects a Jellyfin destination library and, when required, fills path-template variables.
4. The plugin searches configured providers, ranks candidates, and picks or asks for a result.
5. The selected item is sent to a torrent client or hosted-link downloader.
6. The plugin monitors the download, extracts archives when needed, analyzes the media files, organizes them into the target library path, and optionally triggers a Jellyfin scan.

In plain language: the plugin helps a user choose what to download, sends it to the right downloader, watches the progress, unpacks it when needed, and moves the finished media into Jellyfin.

## Main building blocks

- `Telegram/Commands/CommandAutoDownload.cs`
  Entry point for `/autodownload`.
- `Services/Download/DownloadManagerService.cs`
  Background service that restores persisted downloads, runs processing every 10 seconds, and schedules health checks.
- `Services/Download/DownloadOrchestrator.cs`
  Main coordinator for the workflow and status transitions.
- `Services/Download/Search/SearchOrchestrator.cs`
  Fan-out to all enabled search providers, then quality ranking.
- `Services/Download/QualityRuleEngine.cs`
  Scores candidates against the selected library quality profile.
- `Services/Download/ArchiveExtractionService.cs`
  Detects archives and tries passwords in sequence.
- `Services/Download/MediaAnalyzerService.cs`
  Groups downloaded media files and resolves metadata needed for organization.
- `Services/Download/PathTemplateService.cs`
  Expands library path templates and dynamic variables.
- `Services/Download/MediaFileOrganizerService.cs`
  Moves the analyzed media into the final Jellyfin library path.
- `Services/Download/ServiceHealthMonitor.cs`
  Tracks downloader health and filters out unhealthy services.

## Lifecycle

### 1. Workflow bootstrap

`/autodownload tt1234567` is currently the intended entry point.

The command:

- validates the IMDb ID format,
- creates a managed download entry via the orchestrator,
- asks the user to choose a destination Jellyfin library,
- then continues through any additional missing workflow state.

The current implementation is IMDb-centric by design. That aligns with the newer search path as well, where providers can receive both a human query and the IMDb ID and may prefer IMDb-first lookups when it improves reliability.

### 2. State machine

The managed download progresses through `DownloadStatus` values:

- `Pending`
- `AwaitingMediaType`
- `AwaitingSeason`
- `AwaitingLibrary`
- `AwaitingSearchResult`
- `AwaitingPathVars`
- `AwaitingPathConfirm`
- `Downloading`
- `Extracting`
- `ExtractionFailed`
- `Analyzing`
- `Organizing`
- `Completed`
- `Canceled`
- `Failed`
- `Stalled`

Not every download touches every interactive state, but the enum reflects the intended full workflow surface.

### 3. Search and selection

The search stage now works like this:

- `SearchOrchestrator` invokes all enabled `ISearchProvider` implementations.
- Providers receive `query` and `imdbId`.
- Providers are expected to return actionable `SearchResult` entries, not just rough title hits.
- Results are scored with `QualityRuleEngine` and filtered by the target library `QualityProfile`.

Important current behavior:

- Some providers are more reliable when searched by IMDb ID first.
- Structured providers now crawl the actual result page and extract the real download target.
- Passwords exposed on the page are carried forward into the download record for archive extraction.
- Hosted downloads may now carry multi-link payloads when a provider exposes several direct parts instead of one container link.

### 4. Download execution

The orchestrator does not hardcode a single backend. It selects from:

- torrent services:
  `Transmission`, `qBittorrent`
- hosted-link services:
  `JDownloader2`, `pyLoad`

Selection is filtered by:

- the service being enabled in config,
- the service being healthy enough according to `ServiceHealthMonitor`,
- the content type and download method required by the selected search result.

### 5. Extraction

If the staging area contains archives, `ArchiveExtractionService`:

- detects common archive extensions and simple multipart patterns,
- tries configured passwords,
- now also tries any password extracted from the source search result,
- falls back to no-password last.

The extraction service now includes free-space checks, optional archive cleanup, multipart archive handling, and a configurable limit for nested archives. Progress reporting is still approximate because it depends on what the archive reader can report.

### 6. Analysis and organize

After download and optional extraction:

- `MediaAnalyzerService` identifies usable video and subtitle files,
- infers season or episode details where needed,
- `PathTemplateService` expands the selected library template,
- `MediaFileOrganizerService` moves files into the final library destination,
- Jellyfin library scanning can be triggered afterward depending on configuration.

## Background behavior

The hosted service starts automatically with the plugin and does three important things:

- restores unfinished managed downloads on startup,
- processes all downloads every 10 seconds,
- performs service health checks on startup and then on the configured interval.

That means the download manager is designed to survive plugin restarts. Recovery is functional for saved records, but the detailed restart behavior still needs more end-to-end testing for every workflow state.
