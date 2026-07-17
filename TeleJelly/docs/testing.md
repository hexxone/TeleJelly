# Testing

This page explains the current test setup. The normal test suite is meant for everyday development. Live provider checks and full Docker-based smoke tests are heavier and should be run only when needed.

## Local test suite

Run this from the `TeleJelly/` directory:

```bash
dotnet tool restore
dotnet test TeleJelly.sln
```

To create a coverage report:

```bash
dotnet tool restore
dotnet test TeleJelly.sln --collect:"XPlat Code Coverage;Format=cobertura" --results-directory TestResults
dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:"HtmlSummary;Cobertura"
```

The current suite gives the best coverage for:

- `Services/Download/Search`
- `Services/Download`
- `Telegram`

## CI behavior

CI excludes live provider tests so the pipeline does not depend on external websites:

```bash
dotnet test TeleJelly.sln --filter "TestCategory!=LiveSearch" --collect:"XPlat Code Coverage;Format=cobertura" --results-directory TestResults
```

## Local stack checks

Use [download-manager/60-local-test-stack.md](download-manager/60-local-test-stack.md) when you need to test against real downloader services.

That stack is intended for manual and smoke testing:

- start Jellyfin and the supported downloader backends,
- check that TeleJelly can connect to each enabled backend,
- run one torrent-style flow and one hosted-link flow,
- verify that the final file lands under the selected Jellyfin library path.

These checks are not a replacement for unit tests. They catch integration problems that only appear when containers, shared folders, credentials, and downloader APIs all interact.

## JDownloader2

Get all links:

```bash
curl -H 'Content-Type: application/json' \
  --data '{"params":[{}]}' \
  http://127.0.0.1:3128/downloadsV2/queryLinks
```
