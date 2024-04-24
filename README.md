<p align="center">
<img alt="Logo" src="./TeleJelly/thumb.jpg" height="256"/>
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

Having a Telegram Username is mandatory.

Inspired by [jellyfin-plugin-ldapauth](https://github.com/jellyfin/jellyfin-plugin-ldapauth) and [jellyfin-plugin-sso](https://github.com/9p4/jellyfin-plugin-sso).

Created from [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template).

## Usage

1. User clicks the "Sign In with Telegram" Disclaimer Link on the Jellyfin Login Page
2. User lands on the page "/sso/Telegram/login"
3. Plugin shows a Page with embedded "Telegram Login" button.
4. When the button is clicked, the Plugin validates User credentials using custom Telegram logic.
5. On Success -> Authenticate & User is redirected to Jellyfin Dashboard
6. On Failure -> Show Error Message (e.g. Invalid Data, not Whitelisted)

## Install

Currently only two options for manual install. Sorry - No repository.

### Option 1: Download Release

1. Download the 'latest' Version from Releases on the right
2. put files into `config/plugins/TeleJelly` folder
3. restart jellyfin

There is an example config included, but it will also get created automatically if you'd prefer editing the UI.

### Option 2: Compile by yourself

You can also compile the Plugin by yourself if you dont trust the download.

1. `git clone https://github.com/hexxone/TeleJelly.git` or download Repo as zip.
2. install [.NET6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
3. run `publish.ps1`
4. put `./publish/` files into `config/plugins/TeleJelly` folder
5. restart jellyfin

## Setup

Go to the Plugin configuration page and fill in the Bot-Token first.

Make sure to use the `/setdomain` command to link your jellyfin domain (needs valid SSL cert).

Afterwards you can add yourself into the "Administrators" list for full access, or create a Group.

Now you should be able to log in to jellyfin by visiting `yourjellyfin.com/sso/Telegram/login`.

If you are running Jellyfin Version >= 10.8, you may also include this link in the "Branding" via Markdown or HTML.

E.g.: `[Telegram-Login](https://jelly.fin/sso/telegram/login)`

## Features

- SSO Login page (at /sso/Telegram/Login)
  - styled similar to the regular login page
  - shows a "Back to Normal Login" button
  - shows the Telegram Login Widget
  - checks the Telegram Auth data against the backend
  - if data is invalid -> show error message
  - if data is valid -> takes the Jellyfin Auth Response and authenticates the user
  - loading animation

- Config page (reachable via Jellyfin Plugin Page)
  - allows setting the Telegram Bot Token (required)
  - allows setting a List of Administrator Telegram Usernames (get full Access)
  - allows forcing an external Protocol Scheme (for problems with reverse Proxies)
  - allows Creating/Editing/Deleting a "virtual" management Group
    - Grants access to all OR specific Libraries for non-Administrators.
    - A user needs to be Admin OR part of at least ONE Group to Log-in.

- uses "ILRepack" for packing the multiple dependency dlls into one single plugin dll

## Known issues

The "Sign In with Telegram" button will sometimes get hidden by Browser Plugins like "I dont like Cookies" or "UBlock Origin".
Try disabling these for your Jellyfin domain.

If a User's profile picture fails to download even though the url is given (err 404), he has probably set it to private.
In this case, we will set the default TeleJelly plugin icon.

If a User were to change/sell his Username, a random person would possibly be able to use this Service, but having Names over ID's is much more convenient for Management.

If your server is publicly reachable, make sure to take care of rate limiting with your reverse proxy! Otherwise adversaries might be able to lag the system.

## Development

1. Clone Repo
2. Make sure to install "[JellyFin Server](https://repo.jellyfin.org/releases/server/windows/stable/)" for debugging on Windows. Keep the default path.
3. Open Solution file, restore packages
4. Run Debug
5. Open http://localhost:8096

## Screenshots (outdated)

### Login Page

![Login Page](./screenshots/01.jpg)

### Config Page

![Config Page 1](./screenshots/02.jpg)

![Config Page 2](./screenshots/03.jpg)

## Licensing

Licensing is a complex topic. This repository features a GPLv3 license template that can be used to provide a good default license for your plugin. You may alter this if you like, but if you do a permissive license must be chosen.

Due to how plugins in Jellyfin work, when your plugin is compiled into a binary, it will link against the various Jellyfin binary NuGet packages. These packages are licensed under the GPLv3. Thus, due to the nature and restrictions of the GPL, the binary plugin you get will also be licensed under the GPLv3.

If you accept the default GPLv3 license from this template, all will be good. However if you choose a different license, please keep this fact in mind, as it might not always be obvious that an, e.g. MIT-licensed plugin would become GPLv3 when compiled.

Please note that this also means making "proprietary", source-unavailable, or otherwise "hidden" plugins for public consumption is not permitted. To build a Jellyfin plugin for distribution to others, it must be under the GPLv3 or a permissive open-source license that can be linked against the GPLv3.
