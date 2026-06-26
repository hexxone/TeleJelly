# Download Manager Operations And Troubleshooting

## Runtime behavior

`DownloadManagerService` is the long-running background loop for this feature.

On startup it:

1. restores persisted downloads,
2. performs an initial health check,
3. starts periodic download processing,
4. starts periodic health monitoring.

Important intervals:

- download processing runs every 10 seconds,
- service health checks run on `HealthMonitoring.CheckIntervalMinutes`.

## Service selection and health

`ServiceHealthMonitor` keeps per-service health state and exposes available services back to the orchestrator.

Current service priority order:

- torrents:
  `Transmission` before `qBittorrent`
- hosted:
  `JDownloader2` before `pyLoad`

Health state meanings:

- `Online`
  connection checks are succeeding.
- `Degraded`
  some failures occurred but the service is still considered selectable.
- `Offline`
  the service exceeded the configured consecutive-failure threshold and should no longer be selected.

## API endpoints

`Controller/DownloadManagerController.cs` currently exposes:

- `GET /TeleJelly/DownloadManager/downloads`
  Returns all managed downloads, optionally filtered by `?status=...`.
- `GET /TeleJelly/DownloadManager/health`
  Returns current service health snapshots.
- `POST /TeleJelly/DownloadManager/downloads/{id}/cancel`
  Marks a managed download as canceled.
- `DELETE /TeleJelly/DownloadManager/downloads/{id}`
  Placeholder only right now. It does not yet perform full removal and cleanup.

The delete endpoint should not be treated as a complete management API yet.

## Common failure points

### Search returns poor or no results

Check:

- `Search.Enabled` is true,
- the provider name is listed in `Search.EnabledServices`,
- the provider itself is one of the currently workable adapters,
- the item was started with a valid IMDb ID,
- the target library quality profile is not filtering everything out.

Use [../search-recon/00-index.md](../search-recon/00-index.md) to verify whether the provider is currently implemented through the structured crawler path or still unresolved.

### Download never starts

Check:

- at least one downloader is enabled,
- downloader credentials are valid,
- the service is not `Offline`,
- the selected search result contains the expected actionable payload for that backend,
- staging paths are correct and writable from the runtime environment.

### Archive extraction fails

Check:

- extraction is enabled,
- the archive was actually downloaded into the staging area,
- multipart sets are complete,
- the provider password was captured,
- the fallback password list is populated with realistic values.

Known current limits:

- no pre-extraction free-space guard,
- simplistic multipart detection,
- no recursive extraction policy yet,
- no built-in cleanup of source archives after success.

### Final organize step fails

Check:

- `LibrarySettings` contains a matching target library,
- the path template expands into a valid path,
- dynamic variables are defined and filled,
- the final destination is writable,
- file naming still allows season or episode inference where required.

## Operational notes for future work

- The controller now supports cancel, retry, remove, and remove-with-file-cleanup flows for download records.
- Health monitoring now keeps services in runtime `Online`/`Degraded`/`Offline` state and sends a one-shot Telegram warning when a service drops offline.
- Archive extraction is wired into the normal lifecycle before analysis and organization. Failed retries preserve password-attempt context on the managed download record.
