# Download Manager Setup And Configuration

## Minimum setup checklist

For a local all-in-one development environment, start with [60-local-test-stack.md](60-local-test-stack.md).

To get the feature working end to end, configure all of the following:

1. Enable the download manager itself.
2. Provide a valid `TmdbApiKey`.
3. Enable at least one downloader:
   `Transmission`, `qBittorrent`, `JDownloader2`, or `pyLoad`.
4. Ensure the downloader staging paths are valid and reachable from the environment running the Jellyfin plugin.
5. Configure at least one target Jellyfin library in `LibrarySettings`.
6. If provider search should be used, enable search and list the allowed provider names in `Search.EnabledServices`.

If any of those pieces are missing, the workflow will usually stall in an interactive or failed state rather than finishing cleanly.

## Core config object

The root config type is `DownloadManagerSettings` in `Classes/Configuration/DownloadManagerSettings.cs`.

Important top-level fields:

- `Enabled`
  Master switch for the feature.
- `TmdbApiKey`
  Required for metadata enrichment and normal orchestration.
- `MaxDownloadSizeBytes`
  Hard ceiling for accepted downloads when the result size is known.
- `DownloadTimeoutMinutes`
  Timeout budget for download progression.
- `MaxConcurrentDownloads`
  Upper bound for simultaneous active backend downloads. Additional jobs stay queued in `Pending`.
- `StalledNoSeedsTimeoutMinutes`
  Torrent stall threshold when the backend reports no seeds or peers.
- `StalledNoProgressTimeoutMinutes`
  General stall threshold when progress stops.
- `AutoRemoveCompletedAfterDays`
- `AutoRemoveCompletedDays`
- `AutoRemoveFailedAfterDays`
- `AutoRemoveFailedDays`
- `WhitelistUsernames`
  Restrict who may use the feature.
- `TriggerLibraryScanAfterOrganize`
- `LibrarySettings`
  Per-library destination rules and quality preferences.

## Downloader configuration

### Torrent services

`TorrentServices` currently supports:

- `Transmission`
- `QBittorrent`

Each service needs:

- `Enabled`
- host or endpoint details
- credentials where required
- a `StagingPath`

The staging path matters operationally. The plugin expects downloaded files to appear there so later extraction, analysis, and organization can proceed.

### Hosted-link services

`HostedServices` currently supports:

- `JDownloader2`
- `PyLoad`

Each service also needs:

- `Enabled`
- connection details
- credentials
- `StagingPath`

Recent change worth knowing:

- hosted services now accept multi-link payloads, not just a single URL, because some providers expose several direct hoster parts instead of one file container.

## Search configuration

The search section is intentionally small:

- `Search.Enabled`
- `Search.EnabledServices`

`EnabledServices` must match provider `Name` values, for example:

- `funxd.site`
- `jjs.page`
- `hd-source.to`
- `ddl-warez.cc`
- `movieblog.to`
- `hdencode.org`

Not every registered provider is fully implemented. The live status and site-specific behavior are documented in [../search-recon/00-index.md](../search-recon/00-index.md).

## Extraction configuration

`ExtractionSettings` controls archive handling:

- `Enabled`
- `Passwords`
- `ExtractPasswordsFromDlc`
- `NotifyOnFailure`

Operational note:

- the code now also tries passwords scraped from the source provider page and stored on the managed download.
- the static password list is still useful as a fallback for providers that hide or omit passwords.
- managed downloads also persist whether extraction is required and how many password candidates were attempted so failed extractions can be retried with context.

The default placeholder passwords in the config model should be treated as development defaults, not production-ready values.

## Health monitoring configuration

`HealthMonitoringSettings` controls whether download services stay eligible:

- `Enabled`
- `CheckIntervalMinutes`
- `MaxConsecutiveFailures`

Current implementation detail:

- the monitor marks services `Online`, `Degraded`, or `Offline`,
- unhealthy services are filtered out by the health monitor before selection,
- when a service crosses the offline threshold, the Telegram bot sends a one-time warning to linked chats so admins can investigate instead of mutating saved service config.

## Library configuration

Each `LibrarySettings` item defines how finished media is organized:

- `LibraryId`
- `LibraryName`
- `PathTemplate`
- `DynamicVariables`
- `QualityProfile`

`QualityProfile` scoring now also uses:

- preferred audio codecs
- detected bitrate bonus
- age-aware ranking via `GetScoringBreakdown()`

### Path templates

Supported built-in placeholders currently include:

- `{title}`
- `{year}`
- `{imdbId}`
- `{filename}`
- `{ext}`
- `{season}`
- `{season:00}`
- `{episode}`
- `{episode:00}`

Dynamic placeholders use square-bracket syntax, for example:

- `[Edition]`
- `[Language]`

Those variables must also be defined in `LibrarySettings.DynamicVariables`, otherwise they remain unresolved and are stripped later with a warning.

### Quality profiles

The per-library `QualityProfile` drives search-result ranking and filtering.

It includes preferences and constraints for:

- resolution,
- minimum and maximum size by resolution,
- required and preferred audio languages,
- required and preferred subtitle languages,
- codec,
- HDR format,
- source type,
- minimum seeders,
- scoring weights.

That means two libraries can search the same provider set and still prefer different results.

## Practical setup advice

- Use IMDb IDs as your normal entry point. The current workflow and several providers behave better with that input.
- Start with one torrent client and one hosted-link client before enabling every backend.
- Make sure the staging paths and final library paths are visible from the same runtime context. Path mismatches are a common source of silent downstream failures.
- Keep the enabled provider list short at first and add more once the basic pipeline is stable.
- Treat search-provider behavior as site-specific and unstable. Use the recon docs when enabling or debugging providers.
