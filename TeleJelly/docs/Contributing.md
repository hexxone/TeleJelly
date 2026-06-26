# Contributing

When implementing a new feature, please name your commit messages in a meaningful way and refer to git best practices.

The plugin uses "MinVer" and git-tags for semantic versioning.

Most of the Versions (meta.json and manifest.json) get incremented automatically on release build,
**but** there are some places that have to be done manually - for example, in the `config.html`.

When incrementing the version of Jellyfin, remember to set the correct `TargetAbi` version in `JellyfinPluginHelper`!

> Feel free to open a new Pull-Requests for useful additions and fixes.

## Required/Recommended Tools & Frameworks

- [git](https://git-scm.com/downloads) pulling & pushing.. duh
- [Visual Studio](https://visualstudio.microsoft.com/de/downloads/) or Rider IDE for editing
- [.NET9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) for Jellyfin >= 10.11
- [.NET8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for Jellyfin 10.9 - 10.10
- [.NET6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) for Jellyfin <= 10.8
- [Docker](https://www.docker.com/products/docker-desktop/) for local testing

## Getting Started

1. Run `git clone https://github.com/hexxone/TeleJelly.git`
2. Open `TeleJelly.sln` file with Visual Studio or Rider IDE, restore Nuget packages
3. Copy `example.env`-file to `.env` and fill out the variables
4. From the `TeleJelly/` folder, run `docker compose up -d` for the local stack
5. If you want the bundled HTTPS reverse-proxy example as well, run `docker compose --profile proxy up -d`
6. Afterward Jellyfin with TeleJelly should be reachable under: <http://localhost:8096/> or, with the proxy profile, under <https://jellyfin.localhost:8443/>

> Note: the "invalid" SSL certificate warning is normal.
> You can, however, get a "real" one working with traefik with ease.

## Release Process

This project uses a GitHub Actions workflow for automated releases (`dotnetcore.yml`).
Here is how it ties into the release cycle:

1. **Trigger**: The workflow is triggered manually via the **Actions** tab on GitHub.
2. **Versioning**: It uses `MinVer` to calculate the version number based on the latest Git tag in the history.
3. **Build**: The plugin is compiled using the .NET 8 SDK.
4. **Repository Update**: It automatically updates the `manifest.json` in the orphaned `dist` branch. This allows
   Jellyfin instances to see the new version immediately via the repository URL.
5. **GitHub Release**: Finally, it creates a new Release entry on GitHub, drafts the changelog, and attaches the
   compiled `.zip` file for manual download.
