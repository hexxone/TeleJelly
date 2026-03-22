# TeleJelly TODO List

## High Priority - Download Manager Core Features

### Missing Download Service Implementations
- [ ] **QBittorrentService** (ITorrentDownloadService)
  - [ ] Implement Web API integration
  - [ ] Add/remove torrent methods
  - [ ] Progress tracking
  - [ ] File listing
  - [ ] Test connection

- [ ] **PyLoadService** (IHostedDownloadService)
  - [ ] Implement pyLoad REST API integration
  - [ ] .dlc file handling
  - [ ] Add/remove download methods
  - [ ] Progress tracking
  - [ ] Test connection

### Missing Search Provider Implementations
- [ ] **ISearchProvider implementations** (for automated downloads)
  - [ ] Jackett integration
  - [ ] Prowlarr integration
  - [ ] Consider these providers:
    - https://funxd.site/
    - https://jjs.page/?s=
    - https://hd-source.to/
    - https://filmfans.org/
    - https://serienfans.org/
  - [ ] Untested providers (evaluate first):
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
  - [ ] Multi-step interactive workflow (media type, season, library selection)
  - [ ] Search execution across all enabled providers
  - [ ] Result ranking with QualityRuleEngine
  - [ ] Top 5 results display with inline buttons
  - [ ] Dynamic path variable collection
  - [ ] Path confirmation workflow
  - [ ] Auto-start best match option

- [ ] **CommandDownloadSetPath** - Manual path override command
  - [ ] Parse and validate custom paths
  - [ ] Update download destination
  - [ ] Trigger download start

### Missing Telegram Integrations
- [ ] **Callback Query Handlers** (TelegramBotService)
  - [ ] `dl_{id}_library_{libraryId}` - Library selection
  - [ ] `dl_{id}_mediatype_{type}` - Media type selection (Movie/Series)
  - [ ] `dl_{id}_season_{season}` - Season selection
  - [ ] `dl_{id}_result_{index}` - Search result selection
  - [ ] `dl_{id}_pathvar_{name}_{value}` - Dynamic path variable selection
  - [ ] `dl_{id}_accept` - Accept suggested path
  - [ ] `dl_{id}_edit` - Edit path manually
  - [ ] `dl_{id}_cancel` - Cancel download
  - [ ] `dl_{id}_retry_extraction` - Retry archive extraction with new password

- [ ] **.torrent File Auto-Detection Handler**
  - [ ] Document handler for .torrent file uploads
  - [ ] Prompt user for IMDB ID
  - [ ] Store pending torrent temporarily
  - [ ] Initiate workflow when user replies with IMDB ID

### Service Health & Reliability
- [ ] **Service Health Monitoring**
  - [ ] Periodic health checks (every 5 minutes)
  - [ ] Service status tracking (Online/Offline/Degraded)
  - [ ] Auto-retry with exponential backoff
  - [ ] Health status display in /download_status command

- [ ] **Service Fallback Logic**
  - [ ] Priority ordering for torrent services (Transmission → qBittorrent)
  - [ ] Priority ordering for hosted services (JDownloader2 → pyLoad)
  - [ ] Automatic fallback on service failure
  - [ ] Load balancing across services

### Thread-Safety & Crash Recovery
- [ ] **DownloadOrchestrator Thread-Safety** (from class TODO line 20-21)
  - [ ] Add locks/semaphores for concurrent access
  - [ ] Make state transitions atomic
  - [ ] Add crash recovery mechanism
  - [ ] Ensure downloads can resume after plugin/Jellyfin restart

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

### Download Manager UI
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
  - [ ] Add UI for all 4 download services (Transmission, qBittorrent, JDownloader2, pyLoad)
  - [ ] Add UI for extraction settings (password list management)
  - [ ] Add UI for search settings (enable/disable providers)
  - [ ] Add UI for per-library quality profiles
  - [ ] Add UI for per-library path templates with dynamic variables
  - [ ] Add UI for scoring weights customization

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

## Optional / Future Considerations

- [ ] Settings page improvements:
  - [ ] Add 4 setting checkboxes for "Notify Episodes/Seasons/Series/Movies" (remove single "notify new content" checkbox)
  - [ ] Fix flex layout of group management in desktop/mobile modes

- [ ] Alternative integrations:
  - [ ] Consider Seer (integrates with Sonarr/Radarr) - https://github.com/seerr-team/seerr
  - [ ] Evaluate vs. torrents vs. JDownloader tradeoffs
  - [ ] German torrents harder to find vs. EngSub anime preference

- [ ] Discord integration:
  - [ ] Research Discord OAuth plugins
  - [ ] Evaluate group/permission management in Discord OAuth context

## Notes

- **Torrent vs Hosted**: Torrents preferred for anime (EngSub), JDownloader better for German content (despite captcha issues)
- **Search Providers**: Focus on German DDL sites (filmfans.org, serienfans.org, hd-source.to, funxd.site)
- **Priority**: Get automated download workflow (CommandAutoDownload) working first, then add search providers
- **Testing**: Create comprehensive test environment with all 4 services before production use
