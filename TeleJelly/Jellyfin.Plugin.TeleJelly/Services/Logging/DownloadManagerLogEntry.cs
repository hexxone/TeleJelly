using System;

namespace Jellyfin.Plugin.TeleJelly.Services.Logging;

public sealed class DownloadManagerLogEntry
{
    public DateTime TimestampUtc { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
