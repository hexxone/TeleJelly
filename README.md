# TeleJelly Plugin

A Plugin for logging into [Jellyfin](https://jellyfin.org/) using the [Telegram Login Widget](https://core.telegram.org/widgets/login) as "SSO" provider.

The plugin requires assinging a telegram user to one or more virtual groups in order for him to Login.

Each group can then have access to all or specified Jellyfin-folders.

__Important__:

The user whitelisting and group assignment is done __using Telegram Usernames__. In other words: __having a Telegram Username is mandatory__.
It would have been possible to use User-Id's as fallback but they are rather ugly in the UI and its annoying to manage long numeric lists.
So that's a rather small inconvenience imho.

Some code yoinked from:

- <https://github.com/jellyfin/jellyfin-plugin-template>
- <https://github.com/9p4/jellyfin-plugin-sso>

## Scenario

1. a user wants to access Jellyfin
2. user lands on the page "yourjellyfin.com/sso/Telegram/login" (you have to get him there)
3. TeleJelly Plugin shows a Page with Single "Telegram Login" button.
3.1 The Widget gets instructed to redirect to url on success: "yourfellyfin.com/sso/Telegram/confirm?user=123123&name=sdfsd.....&hash=asdasdasdasd"
4. When the button is clicked, The plugin redirects to the URL with filled parameters.
5. TeleJelly Plugin tries to validate the User data using custom Telegram logic.
6. On Success -> set user Cookie tg_data (24hrs);  SET JELLYFIN LOGIN ???;  redirect to Jellyfin Dashboard
7. On Failure -> redirect to Step 2.

## Installation

WIP.

## Usage

After installing, reboot your Jellyfin server.

Then go to the configuration page and fill in the Bot-Token and Bot-Username first.

Aferwards you can add yourself into the "Administrators" list for full access, or create a Group.

Now you should be able to log in to jellyfin by visiting `yourjellyfin.com/sso/Telegram/login`.

## Done

- Create basic project
- Create GUID
- make it work for Telegram
- fix jellyfin login via Localstorage
  - maybe http / https url in Authreseponse is the problem? (yes it was)
- fix Font Https
- fix SSO Login page style
- minify manually returned html/css/js
- implement config page
- show ?error message on login if given.

## Todo

- fix 404 download profilepic from tg?
- installation instructions
- publish

## Licensing

Licensing is a complex topic. This repository features a GPLv3 license template that can be used to provide a good default license for your plugin. You may alter this if you like, but if you do a permissive license must be chosen.

Due to how plugins in Jellyfin work, when your plugin is compiled into a binary, it will link against the various Jellyfin binary NuGet packages. These packages are licensed under the GPLv3. Thus, due to the nature and restrictions of the GPL, the binary plugin you get will also be licensed under the GPLv3.

If you accept the default GPLv3 license from this template, all will be good. However if you choose a different license, please keep this fact in mind, as it might not always be obvious that an, e.g. MIT-licensed plugin would become GPLv3 when compiled.

Please note that this also means making "proprietary", source-unavailable, or otherwise "hidden" plugins for public consumption is not permitted. To build a Jellyfin plugin for distribution to others, it must be under the GPLv3 or a permissive open-source license that can be linked against the GPLv3.
