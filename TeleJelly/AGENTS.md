# TeleJelly Project Guide

## Project Overview

TeleJelly is a Jellyfin plugin that enables Telegram-based authentication ("SSO") and bot integration. Users can log into Jellyfin using the Telegram Login Widget, and the plugin provides a Telegram bot for media notifications, search, requests, and group management.

## Tech Stack

- **Language**: C# (.NET 9 for Jellyfin >= 10.11)
- **Framework**: Jellyfin Plugin API
- **Telegram**: Telegram.Bot library
- **Frontend**: Vanilla JavaScript, HTML, CSS (Jellyfin's web UI conventions)

## Project Structure

```
Jellyfin.Plugin.TeleJelly/
├── Assets/
│   ├── Config/           # Plugin configuration page (HTML/JS/CSS)
│   │   ├── config.html   # Main config page structure
│   │   ├── config.js     # Config page logic and API calls
│   │   └── config.css    # Styling for config page
│   └── Login/            # SSO login page assets
├── Classes/
│   ├── Configuration/    # Plugin configuration models
│   └── Models/           # Data models (MediaRequest, DownloadStatus, etc.)
├── Controller/           # ASP.NET API controllers
│   ├── TeleJellyConfigController.cs  # Config API endpoints
│   ├── TeleJellySSOController.cs     # SSO authentication
│   └── DownloadManagerController.cs  # Download management API
├── Services/
│   ├── Download/         # Download manager services
│   │   ├── DownloadOrchestrator.cs
│   │   ├── MediaAnalyzerService.cs
│   │   ├── MediaFileOrganizerService.cs
│   │   └── ...
│   ├── NotificationService.cs
│   └── RequestService.cs
└── Telegram/
    ├── Commands/         # Bot command handlers
    │   ├── CommandSearch.cs
    │   ├── CommandRequest.cs
    │   ├── CommandLink.cs
    │   ├── CommandUnlink.cs
    │   └── ...
    ├── TelegramBotService.cs      # Main bot service
    ├── TelegramBackgroundService.cs
    ├── TelegramGroup.cs           # Group model
    └── TelegramGroupChat.cs       # Linked chat model
```

## Key Concepts

### Authentication Flow
1. User visits `/sso/Telegram`
2. Telegram Login Widget authenticates user
3. Plugin validates auth data using bot token
4. User must be Admin OR member of at least one TeleJelly Group

### Groups & Permissions
- **TelegramGroup**: Virtual group in TeleJelly config with library access settings
- **TelegramGroupChat**: Links a TeleJelly group to an actual Telegram chat
- Users in a group get access to specified Jellyfin libraries
- Admins (configured by username) get full access

### Bot Commands
- `/link` - Link Telegram chat to TeleJelly group (admin only)
- `/unlink` - Unlink Telegram chat (admin only)
- `/search <query>` - Search media
- `/request <imdb>` - Request media by IMDB ID
- `/register` - Register user in group
- `/stats` - Show server statistics

### Inline Queries
When enabled, users can search media in any Telegram chat by typing `@BotUsername query`. Results are filtered by user's library access.

## Configuration (PluginConfiguration.cs)

Key settings:
- `BotToken` - Telegram bot token
- `BotUsername` - Bot username (auto-detected)
- `LoginBaseUrl` - External URL for login links
- `AdminUserNames` - List of admin Telegram usernames
- `TelegramGroups` - List of TeleJelly groups
- `EnableBotService` - Toggle bot background service
- `EnableInlineQueries` - Toggle inline search feature

## Frontend Conventions (config.js)

- Uses `ApiClient` for Jellyfin API calls
- Plugin config via `ApiClient.getPluginConfiguration(pluginUniqueId)`
- Custom endpoints via `ApiClient.ajax({ url: ApiClient.getUrl("/api/...") })`
- UI updates via DOM manipulation
- `Dashboard.alert()` for notifications
- `Dashboard.processPluginConfigurationUpdateResult()` after saves

## API Endpoints

### TeleJellyConfigController (`/api/TeleJellyConfig/`)
- `POST ValidateBotToken` - Validate bot token
- `GET GetRequests` - Get media requests
- `POST AddRequest` - Add manual request
- `DELETE RemoveRequest/{imdbId}` - Remove request
- `POST UnlinkGroup/{groupName}` - Unlink Telegram chat from group

### DownloadManagerController (`/TeleJelly/DownloadManager/`)
- `GET downloads` - List active downloads
- `POST downloads/{id}/cancel` - Cancel download

## Build Commands

```bash
# Build the plugin
dotnet build Jellyfin.Plugin.TeleJelly

# Publish release
dotnet publish Jellyfin.Plugin.TeleJelly -c Release

# Run with Docker (development)
docker-compose up
```

## Development Notes

### Adding a New Bot Command
1. Create class in `Telegram/Commands/` implementing `ICommandBase`
2. Set `Command` property (trigger word)
3. Set `NeedsAdmin` if admin-only
4. Implement `Execute()` method
5. Commands are auto-discovered via reflection

### Adding Config Options
1. Add property to `PluginConfiguration.cs`
2. Add UI element in `config.html`
3. Load/save in `config.js` (`populateConfiguration` / `saveConfig`)

### Modifying the Config Page
- HTML structure in `config.html`
- JavaScript logic in `config.js` (tgConfigPage object)
- Styles in `config.css`
- Use Jellyfin's `emby-*` components for consistency

## Common Patterns

### Checking User Permissions
```csharp
var isAdmin = config.AdminUserNames.Contains(username);
var userGroups = config.TelegramGroups
    .Where(g => g.UserNames.Contains(username))
    .ToList();
var hasAccess = isAdmin || userGroups.Any();
```

### Saving Configuration
```csharp
TeleJellyPlugin.Instance!.SaveConfiguration(config);
```

### Bot Client Access
```csharp
var botClient = telegramBotService.BotClientWrapper.Client;
await botClient.SendMessage(chatId, "message", cancellationToken: ct);
```

## File Naming Conventions

- Controllers: `*Controller.cs`
- Services: `*Service.cs`
- Bot Commands: `Command*.cs`
- Models: Descriptive names in `Classes/Models/`

## Testing

Local testing with Docker:
1. Copy `example.env` to `.env` and configure
2. Run `docker-compose up`
3. Access Jellyfin at `https://jellyfin.localhost:8443/`

## Versioning

- Uses MinVer for semantic versioning via git tags
- Version auto-updated in `meta.json` on build
- Manual version updates needed in `config.html`
