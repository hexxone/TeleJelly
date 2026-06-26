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

public interface IRequestService
{
    /// <summary>
    ///     Gets a snapshot of all requests.
    /// </summary>
    Task<IReadOnlyList<MediaRequest>> GetRequestsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces the current list of requests and persists it to disk.
    /// </summary>
    Task SetRequestsAsync(IEnumerable<MediaRequest> requests, CancellationToken cancellationToken);

    /// <summary>
    ///     Tries to add a request while enforcing per-user limits and duplicate checks.
    /// </summary>
    Task<RequestAddResult> TryAddRequestAsync(
        MediaRequest request,
        int maxRequestsPerUser,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Removes a request by IMDb ID.
    /// </summary>
    Task RemoveRequestAsync(string imdbId, CancellationToken cancellationToken);
}

/// <summary>
///     Service for handling media requests, including persisting, retrieving,
///     and managing validations such as user limits and duplicate checks.
/// </summary>
internal sealed class RequestService : IRequestService
{
    private readonly IApplicationPaths _applicationPaths;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private bool _loaded;

    private List<MediaRequest> _requests = [];

    /// <summary>
    ///     Service for handling media requests, including persisting, retrieving,
    ///     and managing validations such as user limits and duplicate checks.
    /// </summary>
    public RequestService(IApplicationPaths applicationPaths, ILogger<RequestService> logger)
    {
        _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string RequestsFilePath =>
        Path.Combine(_applicationPaths.PluginConfigurationsPath, $"{Constants.PluginName}.requests.json");

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

        var needsSave = false;
        RequestAddResult result;

        lock (_lock)
        {
            var normalized = Normalize(request);

            // Check if it already exists
            var existing = _requests.FirstOrDefault(r =>
                string.Equals(r.ImdbId, normalized.ImdbId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // If it exists and is from the same user, toggle (remove) it
                if (string.Equals(existing.UserId, normalized.UserId, StringComparison.Ordinal))
                {
                    _requests.Remove(existing);
                    result = RequestAddResult.Removed;
                    needsSave = true;

                    _logger.LogInformation("Request '{RequestImdbId} - {RequestTitle}' has been removed by User '{UserId}'",
                        request.ImdbId, request.Title, request.UserDisplayName);
                }
                else
                {
                    // If it exists but from another user, report duplicate
                    result = RequestAddResult.Duplicate;
                }
            }
            else
            {
                // Per-user limit check for new requests
                var userRequestCount = _requests.Count(r =>
                    string.Equals(r.UserId, normalized.UserId, StringComparison.Ordinal));

                if (maxRequestsPerUser > 0 && userRequestCount >= maxRequestsPerUser)
                {
                    result = RequestAddResult.UserLimitReached;
                }
                else
                {
                    _requests.Add(normalized);
                    result = RequestAddResult.Added;
                    needsSave = true;

                    _logger.LogInformation("Request '{RequestImdbId} - {RequestTitle}' has been added by User '{UserId}'",
                        normalized.ImdbId, normalized.Title, normalized.UserDisplayName);
                }
            }
        }

        if (needsSave)
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
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

            if (needsSave)
            {
                _logger.LogInformation("Request '{RequestImdbId}' has been removed by System or Administrator", imdbId);
            }
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
            _logger.LogCritical(ex, "Failed to load existing requests file from disk. Starting with empty list.");
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
            _logger.LogCritical(ex, "Failed to save requests to disk. Missing permission or disk full?");
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
    Removed,
    UserLimitReached,
    Error
}
