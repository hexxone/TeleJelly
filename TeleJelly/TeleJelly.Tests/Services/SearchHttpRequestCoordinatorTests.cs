using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Component")]
public class SearchHttpRequestCoordinatorTests
{
    [Test]
    public async Task RetryAfter_CoolsOnlyAffectedOriginWhileOtherOriginContinues()
    {
        var slowFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowRequestCount = 0;
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            if (request.RequestUri!.Host == "slow.example")
            {
                slowFirstRequest.TrySetResult();
                if (Interlocked.Increment(ref slowRequestCount) == 1)
                {
                    var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return throttled;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.Host)
            };
        }));
        var coordinator = new SearchHttpRequestCoordinator(
            maxConcurrentRequests: 2,
            minimumJitter: TimeSpan.Zero,
            maximumJitter: TimeSpan.Zero);
        var fetcher = new HttpClientSearchDocumentFetcher(client, coordinator);

        var slowTask = fetcher.GetStringAsync(new Uri("https://slow.example/search"), CancellationToken.None);
        await slowFirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var fastTask = fetcher.GetStringAsync(new Uri("https://fast.example/search"), CancellationToken.None);

        Assert.That(await fastTask.WaitAsync(TimeSpan.FromMilliseconds(500)), Is.EqualTo("fast.example"));
        Assert.That(slowTask.IsCompleted, Is.False);
        Assert.That(await slowTask.WaitAsync(TimeSpan.FromSeconds(2)), Is.EqualTo("slow.example"));
        Assert.That(slowRequestCount, Is.EqualTo(2));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
