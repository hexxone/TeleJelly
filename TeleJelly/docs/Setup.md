# Setup

If you plan to run jellyfin with the "complete" download-manager stack, there are a lot of steps to follow:

## Preparation

You need:

- required: a (Home-)Server running docker
  - ideally debian/ubuntu x86 (64bit)
  - some free storage
  - (optional) a graphics card for GPU-accelerated Video encoding
- required: a "Public" domain where your users can reach the server
- (optional) a "LAN" domain where you can address your services with in LAN
- (optional) a "Traefik-v3" reverse proxy for routing services via DNS instead of remembering ports

## 1. Setup Jellyfin

1. install the TeleJelly Plugin
2. restart jellyfin
3. go to config Page

- create a Bot via Botfather
- use `/setdomain` command
- get the Bot Token from Botfather
