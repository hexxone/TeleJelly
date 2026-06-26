using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Logging;

/// <summary>
///     Mirrors TeleJelly logs into the in-memory activity log store.
/// </summary>
internal sealed class TeleJellyLoggerProvider : ILoggerProvider
{
    private static readonly string[] _categoryPrefixes =
    [
        "Jellyfin.Plugin.TeleJelly"
    ];

    private readonly IDownloadManagerLogWriter _logWriter;

    public TeleJellyLoggerProvider(IDownloadManagerLogWriter logWriter)
    {
        _logWriter = logWriter;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TeleJellyMirrorLogger(GetMirroredSourceName(categoryName), _logWriter);
    }

    public void Dispose()
    {
    }

    private sealed class TeleJellyMirrorLogger : ILogger
    {
        private readonly IDownloadManagerLogWriter _logWriter;
        private readonly string? _sourceName;

        public TeleJellyMirrorLogger(string? sourceName, IDownloadManagerLogWriter logWriter)
        {
            _sourceName = sourceName;
            _logWriter = logWriter;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _sourceName is not null && logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (_sourceName is null || !IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _logWriter.Write(logLevel, _sourceName, message, exception);
        }
    }

    private static string? GetMirroredSourceName(string categoryName)
    {
        foreach (var categoryPrefix in _categoryPrefixes)
        {
            if (!categoryName.StartsWith(categoryPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (categoryName.Length == categoryPrefix.Length)
            {
                return GetLastSegment(categoryName);
            }

            if (categoryName[categoryPrefix.Length] == '.')
            {
                return categoryName[(categoryPrefix.Length + 1)..];
            }
        }

        return null;
    }

    private static string GetLastSegment(string value)
    {
        var separatorIndex = value.LastIndexOf('.');
        return separatorIndex >= 0 ? value[(separatorIndex + 1)..] : value;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
