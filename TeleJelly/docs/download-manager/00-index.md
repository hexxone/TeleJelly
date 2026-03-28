# Download Manager Docs Index

This folder documents the current `Download Manager` implementation in `Jellyfin.Plugin.TeleJelly`.

Purpose:

- Explain how the feature is used from Telegram through final organization in Jellyfin.
- Capture the actual internal workflow and extension points in the current codebase.
- Document the required setup and the important configuration switches.
- Give the next implementation pass a stable place to start instead of re-discovering behavior.

Files:

- [10-overview-and-workflow.md](10-overview-and-workflow.md)
  End-to-end architecture, state machine, and internal processing flow.
- [20-setup-and-configuration.md](20-setup-and-configuration.md)
  Practical setup checklist and configuration reference.
- [30-operations-and-troubleshooting.md](30-operations-and-troubleshooting.md)
  Runtime behavior, health checks, API endpoints, and common failure points.
- [40-search-and-provider-extension.md](40-search-and-provider-extension.md)
  Search system notes, provider strategy, and rough instructions for adding a new provider.

Related docs:

- [../search-recon/00-index.md](../search-recon/00-index.md)
  Live provider reconnaissance gathered from `curl`/`wget` crawling.
