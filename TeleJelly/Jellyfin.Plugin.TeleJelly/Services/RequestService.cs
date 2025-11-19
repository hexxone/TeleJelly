using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services;

/// <summary>
///     Service for handling media requests, including persisting, retrieving,
///     and managing validations such as user limits and duplicate checks.
/// </summary>
public class RequestService
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<RequestService> _logger;
    private readonly object _lock = new();

    private List<MediaRequest> _requests = [];
    private bool _loaded;

    private string RequestsFilePath =>
        Path.Combine(_applicationPaths.PluginConfigurationsPath, $"{Constants.PluginName}.requests.json");

    /// <summary>
    ///     Service for handling media requests, including persisting, retrieving,
    ///     and managing validations such as user limits and duplicate checks.
    /// </summary>
    public RequestService(IApplicationPaths applicationPaths, ILogger<RequestService> logger)
    {
        _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets a snapshot of all requests.
    /// </summary>
    public async Task<IReadOnlyList<MediaRequest>> GetRequestsAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            return _requests
                .Select(Clone)
                .ToArray();
        }
    }

    /// <summary>
    ///     Replaces the current list of requests and persists it to disk.
    /// </summary>
    public async Task SetRequestsAsync(IEnumerable<MediaRequest> requests, CancellationToken cancellationToken)
    {
        if (requests == null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _requests = requests
                .Where(r => !string.IsNullOrWhiteSpace(r.ImdbId))
                .Select(Normalize)
                .ToList();
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Tries to add a request while enforcing per-user limits and duplicate checks.
    /// </summary>
    public async Task<RequestAddResult> TryAddRequestAsync(
        MediaRequest request,
        int maxRequestsPerUser,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ImdbId))
        {
            throw new ArgumentException("ImdbId is required.", nameof(request));
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        bool needsSave;

        lock (_lock)
        {
            var normalized = Normalize(request);

            // Per-user limit
            var userRequestCount = _requests.Count(r =>
                string.Equals(r.UserId, normalized.UserId, StringComparison.Ordinal));

            if (maxRequestsPerUser > 0 && userRequestCount >= maxRequestsPerUser)
            {
                return RequestAddResult.UserLimitReached;
            }

            // Duplicate by IMDb id
            if (_requests.Any(r =>
                    string.Equals(r.ImdbId, normalized.ImdbId, StringComparison.OrdinalIgnoreCase)))
            {
                return RequestAddResult.Duplicate;
            }

            _requests.Add(normalized);
            needsSave = true;
        }

        if (needsSave)
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return RequestAddResult.Added;
        }

        return RequestAddResult.Error;
    }

    /// <summary>
    ///     Removes a request by IMDb ID.
    /// </summary>
    public async Task RemoveRequestAsync(string imdbId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return;
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        bool needsSave;
        lock (_lock)
        {
            var removedCount = _requests.RemoveAll(r =>
                string.Equals(r.ImdbId, imdbId.Trim(), StringComparison.OrdinalIgnoreCase));
            needsSave = removedCount > 0;
        }

        if (needsSave)
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }
        }

        try
        {
            var path = RequestsFilePath;

            if (!File.Exists(path))
            {
                lock (_lock)
                {
                    _requests = [];
                    _loaded = true;
                }

                return;
            }

            await using var stream = File.OpenRead(path);

            var loaded = await JsonSerializer
                .DeserializeAsync<List<MediaRequest>>(stream, _jsonSerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            lock (_lock)
            {
                _requests = loaded?
                                .Where(r => !string.IsNullOrWhiteSpace(r.ImdbId))
                                .Select(Normalize)
                                .ToList()
                            ?? [];

                _loaded = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load requests file. Starting with empty list.");
            lock (_lock)
            {
                _requests = [];
                _loaded = true;
            }
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        List<MediaRequest> snapshot;
        lock (_lock)
        {
            snapshot = _requests
                .Select(Clone)
                .ToList();
        }

        try
        {
            var path = RequestsFilePath;
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    _jsonSerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save requests to disk.");
        }
    }

    private static MediaRequest Normalize(MediaRequest r)
    {
        return new MediaRequest
        {
            ItemId = r.ItemId,
            ImdbId = r.ImdbId.Trim(),
            Title = r.Title.Trim(),
            Year = r.Year,
            TypeName = r.TypeName?.Trim(),
            ExtraInfo = r.ExtraInfo?.Trim(),
            UserId = r.UserId.Trim(),
            UserDisplayName = r.UserDisplayName.Trim(),
            RequestedAtUtc = r.RequestedAtUtc == default ? DateTime.UtcNow : r.RequestedAtUtc.ToUniversalTime()
        };
    }

    private static MediaRequest Clone(MediaRequest r)
    {
        return new MediaRequest
        {
            ItemId = r.ItemId,
            ImdbId = r.ImdbId,
            Title = r.Title,
            Year = r.Year,
            TypeName = r.TypeName,
            ExtraInfo = r.ExtraInfo,
            UserId = r.UserId,
            UserDisplayName = r.UserDisplayName,
            RequestedAtUtc = r.RequestedAtUtc
        };
    }
}

/// <summary>
///     Result of trying to add a request.
/// </summary>
public enum RequestAddResult
{
    Added,
    Duplicate,
    UserLimitReached,
    Error
}
