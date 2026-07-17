#!/bin/sh
set -eu

plugin_dir=/config/plugins/TeleJelly

mkdir -p "$plugin_dir"
cp -a /opt/telejelly/. "$plugin_dir/"

exec /jellyfin/jellyfin "$@"
