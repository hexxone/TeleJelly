# TeleJelly TODO List

- Issue: <https://github.com/hexxone/TeleJelly/issues/8>
  - User got Stuck on Login Screen without notice or error
  - Try to catch http/https errors as well & show proper message?
  - Add better instructions to README for HTTP/HTTPS/rev-proxy
  - Add better instructions to CONFIG-Page for HTTP/HTTPS/rev-proxy

## High Priority - Download Manager Core Features

### Missing Download Service Implementations
- [x] **QBittorrentService** (ITorrentDownloadService)
  - [x] Implement Web API integration
  - [x] Add/remove torrent methods
  - [x] Progress tracking
  - [x] File listing
  - [x] Test connection

- [x] **PyLoadService** (IHostedDownloadService)
  - [x] Implement pyLoad REST API integration
  - [x] .dlc file handling
  - [x] Add/remove download methods
  - [x] Progress tracking
  - [x] Test connection

### Missing Search Provider Implementations
- [x] **ISearchProvider implementations** (for automated downloads)
  - [x] Implement these providers:
    - https://funxd.site/
    - https://jjs.page/?s=
    - https://hd-source.to/
    - https://filmfans.org/
    - https://serienfans.org/
    - [x] generic provider base scaffold added for shared search behavior
  - [x] Untested providers (evaluate first):
    - https://crawli.net/
    - https://www.data-load.me/
    - https://ddl-warez.cc/
    - https://nima4k.org/
    - https://movieblog.to/
    - https://disco-load.cc/
    - https://hdencode.org/
    - https://byte.to/

### Missing Bot Commands
- [ ] **CommandAutoDownload** - Automated search and download workflow
  - [x] Multi-step interactive workflow (media type, season, library selection)
  - [x] Search execution across all enabled providers
  - [x] Result ranking with QualityRuleEngine
  - [x] Top 5 results display with inline buttons
  - [x] Dynamic path variable collection
  - [x] Path confirmation workflow
  - [ ] Auto-start best match option

- [x] **CommandDownloadSetPath** - Manual path override command
  - [x] Parse and validate custom paths
  - [x] Update download destination
  - [x] Trigger download start

### Missing Telegram Integrations
- [x] **Callback Query Handlers** (TelegramBotService)
  - [x] `dl_{id}_library_{libraryId}` - Library selection
  - [x] `dl_{id}_mediatype_{type}` - Media type selection (Movie/Series)
  - [x] `dl_{id}_season_{season}` - Season selection
  - [x] `dl_{id}_result_{index}` - Search result selection
  - [x] `dl_{id}_pathvar_{name}_{value}` - Dynamic path variable selection
  - [x] `dl_{id}_accept` - Accept suggested path
  - [x] `dl_{id}_edit` - Edit path manually
  - [x] `dl_{id}_cancel` - Cancel download
  - [x] `dl_{id}_retry_extraction` - Retry archive extraction with new password

- [x] **.torrent File Auto-Detection Handler**
  - [x] Document handler for .torrent file uploads
  - [x] Prompt user for IMDB ID
  - [x] Store pending torrent temporarily
  - [x] Initiate workflow when user replies with IMDB ID

### Service Health & Reliability
- [ ] **Service Health Monitoring**
  - [x] Periodic health checks (every 5 minutes)
  - [x] Service status tracking (Online/Offline/Degraded)
  - [x] Auto-retry with exponential backoff
  - [x] Health status display in /download_status command

- [ ] **Service Fallback Logic**
  - [x] Priority ordering for torrent services (Transmission → qBittorrent)
  - [x] Priority ordering for hosted services (JDownloader2 → pyLoad)
  - [x] Automatic fallback on service failure
  - [ ] Load balancing across services

### Thread-Safety & Crash Recovery
- [ ] **DownloadOrchestrator Thread-Safety** (from class TODO line 20-21)
  - [x] Add locks/semaphores for concurrent access
  - [x] Make state transitions atomic
  - [ ] Add crash recovery mechanism
  - [x] Ensure downloads can resume after plugin/Jellyfin restart

## Medium Priority - Improvements & Enhancements

### Archive Extraction Enhancements
From ArchiveExtractionService TODOs:
- [ ] Disk space checking before extraction (100% + 20% margin)
- [ ] Multi-part archive space calculation
- [ ] Accurate progress percentage reporting (currently estimates)
- [ ] Option to delete extracted archives after successful extraction
- [ ] Handle tar.gz/tar.bz2 wrapped archives (recursive extraction)
- [ ] Improve multi-part archive detection (better than current pattern matching)

### JDownloader2 Improvements
- [ ] Test and fix timing issue in `AddDownloadAsync()` (5-second delay may be insufficient)
- [ ] Implement `ExtractPasswordFromDlcAsync()` (currently returns null)
- [ ] Add retry logic for failed link grabbing

### Settings Page & Download Manager UI
- [ ] Settings page improvements:
    - [ ] Add 4 setting checkboxes for "Notify Episodes/Seasons/Series/Movies" (remove single "notify new content" checkbox)
    - [ ] Fix flex layout of group management in desktop/mobile modes
- [ ] **Plugin Configuration Page UI**
  - [ ] Download queue view (table with ID, Title, Status, Progress, Service, ETA)
  - [ ] Status filters (All, Downloading, Awaiting Input, Completed, Failed, Stalled)
  - [ ] Sortable columns
  - [ ] Per-download actions (View Details, Cancel, Reset State, Retry, Clean Files)
  - [ ] Bulk actions (Remove Selected, Retry Selected)
  - [ ] Auto-removal status display and countdown
  - [ ] Service health dashboard (4 services: Transmission, qBittorrent, JDownloader2, pyLoad)
  - [ ] Test connection buttons
  - [ ] Active downloads count per service
  - [ ] Consider: Real-time updates with SignalR (optional)

### Configuration Management
- [ ] Add Download Manager settings to plugin configuration UI
  - [ ] Currently only accessible via JSON editing
  - [ ] Add UI for downloads which shows the status correctly for all services (Transmission, qBittorrent, JDownloader2, pyLoad)
  - [ ] Add UI for extraction settings (password list management)
  - [ ] Add UI for search settings (enable/disable providers)
  - [ ] Add UI for per-library quality profiles
  - [ ] Add UI for per-library path templates with dynamic variables
  - [ ] Add UI for scoring weights customization

### Code Cleanup & Interface Usage
- [ ] Review and either use or remove currently-unused download/search interfaces and members
  - [ ] `ITorrentDownloadService`: interface-level TODO and unused members
  - [ ] `IHostedDownloadService`: interface-level TODO and unused members
  - [ ] `IServiceHealthMonitor`: unused member cleanup or implementation
  - [ ] `SearchOrchestrator`: unused TODO cleanup or implementation
  - [ ] `SearchResult`: unused property/member cleanup or usage

### Build & Tooling
- [ ] `JellyfinPluginHelper` improvements
  - [ ] Derive target ABI from installed NuGet version instead of hardcoding `10.11.0.0`
  - [ ] Evaluate replacing current versioning approach with `MinVer.Lib`
  - [ ] Consider packaging helper/build target logic into a reusable NuGet package

## Low Priority - Testing & Documentation

### Testing
- [ ] **Unit Tests**
  - [ ] QualityRuleEngine scoring logic (all edge cases)
  - [ ] PathTemplateService template parsing and variable substitution
  - [ ] MediaAnalyzerService season/episode extraction regex patterns
  - [ ] ArchiveExtractionService password iteration
  - [ ] File grouping logic

- [ ] **Integration Tests**
  - [ ] Full manual download workflow (IMDB → download → extract → organize)
  - [ ] Automated download workflow (search → score → download)
  - [ ] Multi-service fallback (Transmission offline → qBittorrent)
  - [ ] Archive extraction with password-protected test files
  - [ ] .dlc file with embedded password
  - [ ] Multi-part archive extraction (.part01.rar, .r00, etc.)
  - [ ] File conflict handling (file already exists)
  - [ ] Service offline recovery
  - [ ] Extraction stuck → retry with new password workflow

- [ ] **Docker Compose Test Environment**
  - [ ] Add Jellyfin + all 4 download services
  - [ ] Shared staging volumes (separate per service)
  - [ ] Test file permissions (UID/GID alignment)
  - [ ] Add Gluetun VPN example configuration

### Documentation
- [ ] **User Documentation**
  - [ ] Setup guide (Docker Compose configuration for all services)
  - [ ] Command reference (all download commands with examples)
  - [ ] Path template syntax guide with examples
  - [ ] Quality profile configuration guide
  - [ ] IMDB ID lookup guide
  - [ ] Troubleshooting guide (common errors, extraction issues)

- [ ] **Admin Documentation**
  - [ ] Configuration options explained (all 4 services)
  - [ ] Password list management
  - [ ] Quality profile per library setup
  - [ ] Whitelist setup
  - [ ] Service health monitoring

- [ ] **Code Documentation**
  - [ ] XML comments for all public methods
  - [ ] Architecture overview
  - [ ] State machine diagram
  - [ ] Workflow diagrams

### Docker Compose & Infra
- [ ] Improve `docker-compose.yml` examples and service notes
  - [ ] Add/document rate-limiting example
  - [ ] Add and validate `gluetun` integration
  - [ ] Document whether `51XXX` ports are required for RPC or web UI
  - [ ] Clarify whether the relevant service exposes a web UI and whether it should be routed via Traefik

## Optional / Future Considerations

- [ ] Alternative integrations:
    - [ ] Consider Seer (integrates with Sonarr/Radarr) - https://github.com/seerr-team/seerr
    - [ ] Evaluate vs. torrents vs. JDownloader tradeoffs
    - [ ] German torrents harder to find vs. EngSub anime preference
    - [ ] Jackett Search integration
    - [ ] Prowlarr Search integration

## Notes

- **Torrent vs Hosted**: Torrents preferred for anime (EngSub), JDownloader better for German content (despite captcha issues)
- **Search Providers**: Focus on German DDL sites (filmfans.org, serienfans.org, hd-source.to, funxd.site)
- **Priority**: Get automated download workflow (CommandAutoDownload) working first, then add search providers
- **Testing**: Create comprehensive test environment with all 4 services before production use

## Completed Recently ✅

- [x] SearchResult class enhancements (audio/subtitle languages, bitrate, release name, upload date)
- [x] QualityRuleEngine comprehensive scoring logic
  - [x] Hard requirements (seeders, file size, required languages)
  - [x] Preference-based scoring (resolution, codec, HDR, source, languages)
  - [x] Edge case handling (hosted downloads, missing metadata, unknown sizes)
  - [x] Two-factor age-based scoring (absolute freshness + relative spread)
  - [x] Configurable scoring weights
  - [x] Debugging breakdown tool (GetScoringBreakdown)

## Previously Completed ✅

- [x] Telegram Settings UI improvements (hide/show based on link status)
- [x] Settings page layout fixes (vertical fill, IMDB links in request list)
- [x] TransmissionService full implementation
- [x] JDownloader2Service implementation (mostly complete)
- [x] ArchiveExtractionService core functionality
- [x] MediaAnalyzerService full implementation
- [x] MediaFileOrganizerService full implementation
- [x] PathTemplateService full implementation
- [x] DownloadOrchestrator state machine
- [x] DownloadManagerService worker loop
- [x] CommandDownload (manual download with IMDB ID)
- [x] CommandDownloadStatus (global overview)
- [x] CommandDownloadCancel
