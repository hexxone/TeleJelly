using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Logging;

internal sealed class DownloadManagerLogStore : IDownloadManagerLogStore, IDownloadManagerLogWriter
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<DownloadManagerLogEntry> _entries = new();

    public IReadOnlyList<DownloadManagerLogEntry> GetRecent(int limit = 200)
    {
        limit = Math.Clamp(limit, 1, MaxEntries);

        var snapshot = _entries.ToArray();
        if (snapshot.Length <= limit)
        {
            return snapshot;
        }

        return snapshot[^limit..];
    }

    public void Write(LogLevel level, string source, string message, Exception? exception = null)
    {
        if (level == LogLevel.None)
        {
            return;
        }

        var renderedMessage = exception == null
            ? message
            : $"{message} | {exception.GetType().Name}: {exception.Message}";

        if (string.IsNullOrWhiteSpace(renderedMessage))
        {
            return;
        }

        _entries.Enqueue(new DownloadManagerLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level.ToString(),
            Source = source,
            Message = renderedMessage
        });

        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }
    }
}