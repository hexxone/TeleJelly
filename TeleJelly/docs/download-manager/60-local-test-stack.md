# Local Test Stack

This document describes the local "easy" Docker Compose setup for exercising the download manager with real services.

Use:

- [docker-compose.yml](../../docker-compose.yml)

The intent is local reproducibility, not production hardening.

## Included Services

The easy stack includes:

- `jellyfin`
- `transmission`
- `qbittorrent`
- `pyload-ng`
- `jdownloader-2`
- `fixture-http`

All services share `./docker-data/downloads` so the plugin and downloaders see the same staging tree.

## Start The Stack

From the repo's `TeleJelly/` directory:

```bash
docker compose up -d
```

Stop it with:

```bash
docker compose down
```

If you also want to bring up the bundled Traefik HTTPS example:

```bash
docker compose --profile proxy up -d
```

## Port Map

- Jellyfin: `8096`
- Traefik HTTP / HTTPS dashboard profile: `8800`, `8443`
- Transmission Web UI / RPC: `9091`
- Transmission peer: `51413/tcp`, `51413/udp`
- qBittorrent Web UI / API: `8080`
- qBittorrent peer: `6881/tcp`, `6881/udp`
- pyLoad-ng Web UI / API: `8000`
- pyLoad-ng Click'n'Load: `9666`
- JDownloader 2 Web UI: `5800`
- JDownloader 2 direct connect: `3129`
- HTTP fixture server: `18080`

## Shared Paths

The compose file standardizes around these paths:

- TeleJelly plugin container:
  - `/downloads`
- Transmission:
  - `/downloads`
- qBittorrent:
  - `/downloads`
- pyLoad-ng:
  - `/downloads`
- JDownloader 2:
  - `/downloads`
  - `/output`

That matches the TeleJelly default staging paths:

- `/downloads/staging/transmission`
- `/downloads/staging/qbittorrent`
- `/downloads/staging/pyload`
- `/downloads/staging/jdownloader`

## Service Notes

### Transmission

- The stack sets username/password to `telejelly` / `telejelly`.
- Point TeleJelly's Transmission config to host `transmission`, port `9091`.

### qBittorrent

- The LinuxServer image prints a temporary admin password to the container logs on first startup.
- After first login, set a stable username/password in the Web UI before wiring it into TeleJelly.
- Point TeleJelly's qBittorrent config to host `qbittorrent`, port `8080`.

### pyLoad-ng

- Default login is `pyload` / `pyload`.
- Point TeleJelly's pyLoad config to host `pyload-ng`, port `8000`.

### JDownloader 2

- Access the local GUI at `http://localhost:5800`.
- The container has both `/downloads` and `/output` mounted to the same host path so TeleJelly's configured staging path can be used directly.
- If My.JDownloader direct connection is needed, port `3129` is exposed.

## Fixtures

Place local hosted-download fixtures under:

- `./docker-data/fixtures/http`

They will be served by `fixture-http` at:

- `http://localhost:18080/`

This is useful for:

- direct-link hosted-download tests
- archive fixture tests
- DLC and password-protected sample flows

## Recommended Local TeleJelly Settings

Use the service names as hosts from inside the Jellyfin container:

- Transmission host: `transmission`
- qBittorrent host: `qbittorrent`
- pyLoad host: `pyload-ng`

For JDownloader 2, configure the device according to the local JDownloader/My.JDownloader setup you activate in the GUI.

## Current Limitations

- qBittorrent still requires one manual credential bootstrap step after first startup.
- JDownloader 2 remains the least automation-friendly backend for local testing because the integration currently relies on My.JDownloader semantics.
- This stack is intentionally not VPN-routed. Keep it for local development and reproducible testing.
