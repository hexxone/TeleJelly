using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;

internal sealed class HttpClientSearchDocumentFetcher : ISearchDocumentFetcher
{
    internal const int MaxConcurrentRequests = 4;
    internal static readonly TimeSpan MinimumRequestJitter = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan MaximumRequestJitter = TimeSpan.FromMilliseconds(800);

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly SearchHttpRequestCoordinator SharedCoordinator = new();
    private readonly SearchHttpRequestCoordinator _coordinator;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpClientSearchDocumentFetcher>? _logger;

    public HttpClientSearchDocumentFetcher(ILogger<HttpClientSearchDocumentFetcher>? logger = null)
        : this(SharedHttpClient, SharedCoordinator, logger)
    {
    }

    internal HttpClientSearchDocumentFetcher(
        HttpClient httpClient,
        SearchHttpRequestCoordinator coordinator,
        ILogger<HttpClientSearchDocumentFetcher>? logger = null)
    {
        _httpClient = httpClient;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task<SearchDocumentResponse> GetResponseAsync(Uri uri, CancellationToken ct)
    {
        var response = await SendAsync(uri, () => new HttpRequestMessage(HttpMethod.Get, uri), ct);
        return new SearchDocumentResponse(response.StatusCode, response.FinalUri, DecodeText(response.Content, response.Charset));
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        var response = await GetResponseAsync(uri, ct);
        EnsureSuccessStatusCode(response.StatusCode, uri);
        return response.Content;
    }

    public async Task<byte[]> GetBytesAsync(Uri uri, CancellationToken ct)
    {
        var response = await SendAsync(uri, () => new HttpRequestMessage(HttpMethod.Get, uri), ct);
        EnsureSuccessStatusCode(response.StatusCode, uri);
        return response.Content;
    }

    public async Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct)
    {
        var values = formValues.ToArray();
        var response = await SendAsync(
            uri,
            () => new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(values) },
            ct);
        EnsureSuccessStatusCode(response.StatusCode, uri);
        return DecodeText(response.Content, response.Charset);
    }

    private async Task<BufferedHttpResponse> SendAsync(
        Uri uri,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        return await _coordinator.SendAsync(_httpClient, uri, requestFactory, _logger, ct);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TeleJelly/1.0");
        return client;
    }

    private static string DecodeText(byte[] content, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(content);
            }
            catch (ArgumentException)
            {
                // Fall through to UTF-8 for invalid or unavailable charset names.
            }
        }

        return Encoding.UTF8.GetString(content);
    }

    private static void EnsureSuccessStatusCode(HttpStatusCode statusCode, Uri uri)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        throw new HttpRequestException(
            string.Format(CultureInfo.InvariantCulture, "Response status code does not indicate success: {0} ({1}) for {2}.", (int)statusCode, statusCode, uri),
            null,
            statusCode);
    }
}

internal sealed record BufferedHttpResponse(HttpStatusCode StatusCode, Uri FinalUri, byte[] Content, string? Charset);

internal sealed class SearchHttpRequestCoordinator
{
    private const int MaxRetries = 2;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, OriginState> _origins = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestSlots;
    private readonly TimeSpan _minimumJitter;
    private readonly TimeSpan _maximumJitter;

    internal SearchHttpRequestCoordinator(
        int maxConcurrentRequests = HttpClientSearchDocumentFetcher.MaxConcurrentRequests,
        TimeSpan? minimumJitter = null,
        TimeSpan? maximumJitter = null)
    {
        _requestSlots = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _minimumJitter = minimumJitter ?? HttpClientSearchDocumentFetcher.MinimumRequestJitter;
        _maximumJitter = maximumJitter ?? HttpClientSearchDocumentFetcher.MaximumRequestJitter;
    }

    internal async Task<BufferedHttpResponse> SendAsync(
        HttpClient client,
        Uri uri,
        Func<HttpRequestMessage> requestFactory,
        ILogger? logger,
        CancellationToken ct)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var state = _origins.GetOrAdd(origin, _ => new OriginState());

        for (var attempt = 0; ; attempt++)
        {
            await state.Gate.WaitAsync(ct);
            try
            {
                await WaitForOriginCooldownAsync(state, ct);
                await DelayAsync(RandomDelay(_minimumJitter, _maximumJitter), ct);

                HttpResponseMessage response;
                await _requestSlots.WaitAsync(ct);
                try
                {
                    using var request = requestFactory();
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                finally
                {
                    _requestSlots.Release();
                }

                using (response)
                {
                    if (ShouldRetry(response) && attempt < MaxRetries)
                    {
                        var retryDelay = GetRetryDelay(response.Headers.RetryAfter, attempt);
                        state.NotBeforeUtc = DateTimeOffset.UtcNow + retryDelay;
                        logger?.LogWarning(
                            "Search request to {Origin} returned HTTP {StatusCode}; retrying after {RetryDelay} (attempt {Attempt}/{MaxAttempts})",
                            origin,
                            (int)response.StatusCode,
                            retryDelay,
                            attempt + 1,
                            MaxRetries + 1);
                        continue;
                    }

                    var content = await response.Content.ReadAsByteArrayAsync(ct);
                    return new BufferedHttpResponse(
                        response.StatusCode,
                        response.RequestMessage?.RequestUri ?? uri,
                        content,
                        response.Content.Headers.ContentType?.CharSet);
                }
            }
            finally
            {
                state.Gate.Release();
            }
        }
    }

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        return response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable ||
               response.Headers.RetryAfter != null;
    }

    private static TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        TimeSpan delay;
        if (retryAfter?.Delta is { } delta)
        {
            delay = delta;
        }
        else if (retryAfter?.Date is { } date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }
        else
        {
            delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)) + RandomDelay(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        if (delay < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }

    private static async Task WaitForOriginCooldownAsync(OriginState state, CancellationToken ct)
    {
        var delay = state.NotBeforeUtc - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await DelayAsync(delay, ct);
        }
    }

    private static TimeSpan RandomDelay(TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        return minimum + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * (maximum - minimum).TotalMilliseconds);
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
    }

    private sealed class OriginState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        internal DateTimeOffset NotBeforeUtc { get; set; }
    }
}
