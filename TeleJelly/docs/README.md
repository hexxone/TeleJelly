# TeleJelly Docs

A general overview of the project can be found in the [README.md-file](../../README.md) in the project root.

This only covers the technical details and inner workings.

## Contents

- [Download Manager](./download-manager/00-index.md)
- [Search Reconnaissance](./search-recon/00-index.md)
- [Contributing / Making Changes](Contributing.md)

## Dependencies

- [Telegram.Bot](https://github.com/TelegramBots/telegram.bot) telegram bot api interaction
- [ILRepack](https://github.com/gluck/il-repack) for packing all dependency dlls into one single plugin dll
- [MinVer](https://github.com/adamralph/minver) for automated Release-versioning via git tags

## Repo Notes

- This project currently targets `.NET 9` in the actual codebase, even though some local guidance files mention `.NET 10`.
