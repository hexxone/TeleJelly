<p align="center">
<img alt="Logo" src="https://raw.githubusercontent.com/hexxone/TeleJelly/main/TeleJelly/thumb.jpg" height=256 />
<br/>
<br/>
<a href="https://github.com/hexxone/TeleJelly/blob/main/LICENSE">
<img alt="GPL 3.0 License" src="https://img.shields.io/github/license/hexxone/TeleJelly"/>
</a>
<a href="https://github.com/hexxone/TeleJelly/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/hexxone/TeleJelly"/>
</a>
<a href="https://github.com/hexxone/TeleJelly/releases">
<img alt="Current Release Date" src="https://img.shields.io/github/release-date/hexxone/TeleJelly?color=blue"/>
</a>
<a href="https://github.com/hexxone/TeleJelly/releases">
<img alt="GitHub Downloads" src="https://img.shields.io/github/downloads/hexxone/telejelly/total"/>
</a>
<a href="https://github.com/hexxone/TeleJelly/stargazers">
<img alt="GitHub Repo stars" src="https://img.shields.io/github/stars/hexxone/TeleJelly"/>
</a>
</p>

<h1 align="center">TeleJelly Plugin</h1>

A Plugin for logging into [Jellyfin](https://jellyfin.org/) using the [Telegram Login Widget](https://core.telegram.org/widgets/login) as "SSO" provider.

Allows for simple Group creation/editing/deleting in order to manage Admins/Users/Library-access.

Inspired by [jellyfin-plugin-ldapauth](https://github.com/jellyfin/jellyfin-plugin-ldapauth) and [jellyfin-plugin-sso](https://github.com/9p4/jellyfin-plugin-sso).

Created from [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template).

## Usage

1. User clicks the `Sign in with Telegram` Disclaimer Link on the Jellyfin Login Page
2. User lands on the page `/sso/Telegram`
3. Plugin shows a Page with embedded Telegram Login Widget.
4. When the button is clicked, Plugin validates User credentials using bot token.
    - On Success -> Authenticate & redirect User to Jellyfin Dashboard
    - On Failure -> Show Error Message (e.g. Invalid Data, not Whitelisted)

## Features

- SSO Login page (at `/sso/Telegram`)
    - styled similar to the regular login page
    - responsive / mobile capable
    - shows a "Back to Normal Login" button
    - shows the Telegram Login Widget
    - checks the Telegram Auth data against the backend
    - if data is invalid -> show error message
    - if data is valid -> takes the Jellyfin Auth Response and authenticates the user
    - loading animation
    - supports Custom CSS

- Config page (reachable via Jellyfin Plugin Page)
    - requires setting the Telegram Bot Token
    - allows setting a List of Administrator Telegram Usernames (get full Access)
    - allows forcing an external Protocol Scheme (for reverse proxies like Traefik)
    - allows Creating/Editing/Deleting a "virtual" management Group
        - Grants access to all OR specific Libraries for non-Administrators.
        - _Note: A user needs to be Admin OR part of at least ONE Group to Log-in._

## Requirements

1. A Telegram Username is mandatory for all users who wish to use this Login method.
2. A valid, public SSL certificate is needed for the Login Widget to work (e.g. LetsEncrypt).
3. A Telegram Bot (token) is required to cryptograhpically validate the User Login credentials.

## Installation

You can choose between 3 options below.

### Option 1: Plugin Repository (easy way)

1. Add the repository: <https://raw.githubusercontent.com/hexxone/TeleJelly/dist/manifest.json>
2. install `TeleJelly` from the Plugin catalogue
3. restart jellyfin

### Option 2: Download manually

1. Download the 'latest' [Release](https://github.com/hexxone/TeleJelly/releases)
2. Unzip files into `config/plugins/TeleJelly` folder (create it if non-existing)
3. restart jellyfin

### Option 3: Compile by yourself

Don't trust the downloads?

1. `git clone https://github.com/hexxone/TeleJelly.git` or download as zip.
2. install [.NET6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
3. run `publish.ps1`
4. put `./publish/` files into `config/plugins/TeleJelly` folder
5. restart jellyfin

## Configuration

1. Make a new Bot & get the Token via [@Botfather](https://t.me/BotFather)
2. Make sure to use the `/setdomain` command to link your jellyfin domain (needs valid SSL cert).
3. Go to the Plugin configuration page and fill in the Bot-Token.
4. Add yourself into the "Administrators" list for full access, or create an Administrator Group.
5. Now you should be able to log in via Telegram by visiting `jelly.fin/sso/Telegram`.
6. You may also include this link in the Login "Branding" via Markdown or HTML.

E.g.: `[Telegram-Login](https://jelly.fin/sso/telegram)` or screenshots below.

## Known issues

- The `Sign in with Telegram` button will sometimes get hidden by Browser Plugins like "I don't like Cookies" or "UBlock Origin".
  Try disabling these on your Jellyfin domain and inform your users.

- If a User's profile picture fails to download even though the url is given (err 404), he has probably set it to private.
  In this case, the plugin will fall back to its default icon.

- If a User were to change/sell his Username, a random person would possibly be able to use this Service.
  However, having Names over ID's is much more convenient for Management.

- If your server is publicly reachable, make sure to take care of rate limiting with your reverse proxy,
  otherwise adversaries might be able to lag the system.

## Demo Video

_Note: Video & Screenshots are taken with [my custom css theme](https://gist.github.com/hexxone/f00eecb130fa1ca12b3a4bc43d54e587) applied. The Logo is AI-generated._

<video src='./screenshots/demo.mp4' width=600></video>

## Screenshots

### Login Disclaimer

![Login Disclaimer](./screenshots/00.png)

### Login Page

![Login Page](./screenshots/01.png)

### Config Page

![Config Page 1](./screenshots/02.png)

## Development

1. Clone Repo
2. Make sure to install "[JellyFin Server](https://repo.jellyfin.org/releases/server/windows/stable/)" for debugging on Windows. Keep the default path.
3. Open Solution file, restore packages
4. Build the Plugin in Release mode
5. Select to run "HttpsReverseProxy" AND "jellyfin" (server) at the same time.
6. The plugin gets copied to the server and the browser opens automatically.

## Dependencies

- [Telegram.Bot](https://github.com/TelegramBots/telegram.bot) library for validating bot token & determining bot username
- [ILRepack](https://github.com/gluck/il-repack) for packing all dependency dlls into one single plugin dll
- [MinVer](https://github.com/adamralph/minver) for automated Release-versioning via git tags

## Licensing

Licensing is a complex topic. This repository features a GPLv3 license template that can be used to provide a good default license for your plugin.
You may alter this if you like, but if you do a permissive license must be chosen.

Due to how plugins in Jellyfin work, when your plugin is compiled into a binary, it will link against the various Jellyfin binary NuGet packages.
These packages are licensed under the GPLv3. Thus, due to the nature and restrictions of the GPL, the binary plugin you get will also be licensed under the GPLv3.

If you accept the default GPLv3 license from this template, all will be good. If you however choose a different license, please keep this fact in mind,
as it might not always be obvious that a, e.g. MIT-licensed plugin would become GPLv3 when compiled.

Please note that this also means making "proprietary", source-unavailable, or otherwise "hidden" plugins for public consumption is not permitted.
To build a Jellyfin plugin for distribution to others, it must be under the GPLv3 or a permissive open-source license that can be linked against the GPLv3.
