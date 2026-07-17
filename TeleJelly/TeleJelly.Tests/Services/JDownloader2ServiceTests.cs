using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.HostedDownload;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TeleJelly.Tests.Infrastructure;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class JDownloader2ServiceTests
{
    [Test]
    public async Task ExtractPasswordFromDlcAsync_ReturnsEmbeddedPassword()
    {
        const string xml = "<dlc><passwords>secret123</passwords></dlc>";
        var payload = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)));
        var service = new JDownloader2Service(new NullLogger<JDownloader2Service>());

        var password = await service.ExtractPasswordFromDlcAsync(payload, CancellationToken.None);

        Assert.That(password, Is.EqualTo("secret123"));
    }

    [Test]
    public void CanHandle_AcceptsMultilineHttpLinksOnly()
    {
        var service = new JDownloader2Service(new NullLogger<JDownloader2Service>());

        var valid = service.CanHandle("https://example.org/a\nhttps://example.org/b");
        var invalid = service.CanHandle("https://example.org/a\nmagnet:?xt=urn:btih:test");

        Assert.That(valid, Is.True);
        Assert.That(invalid, Is.False);
    }

    [Test]
    public void CanHandle_AcceptsExistingLocalDlcFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telejelly-test-{Guid.NewGuid():N}.dlc");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var service = new JDownloader2Service(new NullLogger<JDownloader2Service>());

            Assert.That(service.CanHandle(new Uri(path).AbsoluteUri), Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ExtractPasswordFromDlcAsync_ReturnsNullWhenDisabled()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                Extraction = new ExtractionSettings
                {
                    ExtractPasswordsFromDlc = false
                }
            }
        });

        const string xml = "<dlc><passwords>secret123</passwords></dlc>";
        var payload = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)));
        var service = new JDownloader2Service(new NullLogger<JDownloader2Service>());

        var password = await service.ExtractPasswordFromDlcAsync(payload, CancellationToken.None);

        Assert.That(password, Is.Null);
    }

    [Test]
    public async Task LegacyConnectionTest_UsesGetWithPositionalJsonParameters()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128"
                    }
                }
            }
        });
        var handler = new RecordingHandler();
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, handler);

        var connected = await service.TestConnectionAsync(CancellationToken.None);

        Assert.That(connected, Is.True);
        Assert.That(handler.Request, Is.Not.Null);
        Assert.That(handler.Request!.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.Request.RequestUri!.AbsolutePath, Is.EqualTo("/downloadsV2/queryLinks"));
        Assert.That(Uri.UnescapeDataString(handler.Request.RequestUri.Query), Is.EqualTo("?{}"));
    }

    [Test]
    public async Task LegacyAddDownload_DoesNotTreatCrawlerJobIdAsPackageUuid()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128",
                        StagingPath = "/downloads/staging/jdownloader"
                    }
                }
            }
        });
        var handler = new AddDownloadHandler();
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, handler);

        var packageId = await service.AddDownloadAsync("https://example.org/container", CancellationToken.None);

        Assert.That(packageId, Is.EqualTo("1784100864015"));
        Assert.That(handler.PackageQueryContainedCrawlerJobId, Is.False);
    }

    [Test]
    public async Task LegacyAddContainer_AssignsJobIdAndResolvesRemappedDownloadPackage()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128",
                        StagingPath = "/downloads/staging/jdownloader"
                    }
                }
            }
        });
        var handler = new AddContainerHandler();
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, handler);

        var crawlerJobId = await service.AddContainerAsync([1, 2, 3], "DLC", CancellationToken.None);

        Assert.That(crawlerJobId, Is.EqualTo("42"));
        Assert.That(handler.SubmittedAsJobAssignedDataUrl, Is.True);
        Assert.That(handler.SetDirectoryCalled, Is.False);
        Assert.That(handler.MoveToDownloadListCalled, Is.False);

        var progress = await service.GetContainerImportProgressAsync(crawlerJobId, CancellationToken.None);
        Assert.That(progress.IsComplete, Is.True);
        Assert.That(handler.JobLinkFilterUsed, Is.True);

        var packageId = await service.CompleteContainerImportAsync(crawlerJobId, CancellationToken.None);
        Assert.That(packageId, Is.EqualTo("packages:1784100864999"));
        Assert.That(handler.SetDirectoryCalled, Is.True);
        Assert.That(handler.MoveToDownloadListCalled, Is.True);
        Assert.That(handler.DownloadLinkFilterUsed, Is.True);
    }

    [Test]
    public async Task LegacyContainerImports_IsolateOverlappingCrawlerJobs()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128",
                        StagingPath = "/downloads/staging/jdownloader"
                    }
                }
            }
        });
        var handler = new OverlappingContainerHandler();
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, handler);

        // Both jobs exist in LinkGrabber before either one is completed.
        var firstJobId = await service.AddContainerAsync([1], "DLC", CancellationToken.None);
        var secondJobId = await service.AddContainerAsync([2], "DLC", CancellationToken.None);

        Assert.That(firstJobId, Is.EqualTo("101"));
        Assert.That(secondJobId, Is.EqualTo("102"));
        Assert.That((await service.GetContainerImportProgressAsync(firstJobId, CancellationToken.None)).IsComplete, Is.True);
        Assert.That((await service.GetContainerImportProgressAsync(secondJobId, CancellationToken.None)).IsComplete, Is.True);

        // Complete them out of submission order to prove no global snapshot/order dependency.
        var secondPackageId = await service.CompleteContainerImportAsync(secondJobId, CancellationToken.None);
        var firstPackageId = await service.CompleteContainerImportAsync(firstJobId, CancellationToken.None);

        Assert.That(secondPackageId, Is.EqualTo("packages:9002"));
        Assert.That(firstPackageId, Is.EqualTo("packages:9001"));
        Assert.That(handler.AllSubmissionsAssignedJobIds, Is.True);
        Assert.That(handler.MovedLinkBatches, Has.Count.EqualTo(2));
        Assert.That(handler.MovedLinkBatches[0], Is.EqualTo(new long[] { 1002 }));
        Assert.That(handler.MovedLinkBatches[1], Is.EqualTo(new long[] { 1001 }));
    }

    [Test]
    public void LegacyAddDownload_ReportsExistingLinkGrabberRejectionImmediately()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128",
                        StagingPath = "/downloads/staging/jdownloader"
                    }
                }
            }
        });
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, new ExistingRejectedLinkHandler());

        var exception = Assert.ThrowsAsync<DownloadRejectedException>(async () =>
            await service.AddDownloadAsync("https://filecrypt.cc/Container/70E431DA32.html", CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("powcaptcha.com"));
    }

    [Test]
    public async Task LegacyProgress_ReportsUnsupportedCaptchaAsTerminalFailure()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128"
                    }
                }
            }
        });
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, new CaptchaFailureHandler());

        var progress = await service.GetProgressAsync("1784100864015", CancellationToken.None);
        var status = progress?.GetType().GetProperty("Status")?.GetValue(progress) as string;

        Assert.That(status, Does.StartWith("Failed:"));
        Assert.That(status, Does.Contain("powcaptcha.com"));
    }

    [Test]
    public async Task LegacyProgress_FollowsPackageBackToLinkGrabberAndReportsCaptchaFailure()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                HostedServices = new HostedServicesSettings
                {
                    JDownloader2 = new JDownloader2Settings
                    {
                        Enabled = true,
                        ConnectionMode = JDownloader2ConnectionMode.LocalOnly,
                        LocalApiBaseUrl = "http://jdownloader.test:3128"
                    }
                }
            }
        });
        using var service = new LegacyJDownloader2Service(NullLogger.Instance, new LinkGrabberCaptchaFailureHandler());

        var progress = await service.GetProgressAsync("1784103783572", CancellationToken.None);
        var status = progress?.GetType().GetProperty("Status")?.GetValue(progress) as string;

        Assert.That(status, Does.StartWith("Failed:"));
        Assert.That(status, Does.Contain("powcaptcha.com"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class AddDownloadHandler : HttpMessageHandler
    {
        private string? _packageName;

        public bool PackageQueryContainedCrawlerJobId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var decodedQuery = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?'));

            if (path == "/linkgrabberv2/addLinks")
            {
                using var document = JsonDocument.Parse(decodedQuery);
                _packageName = document.RootElement.GetProperty("packageName").GetString();
                return JsonResponse(new { data = new { id = 42 } });
            }

            if (path == "/downloadsV2/queryPackages")
            {
                PackageQueryContainedCrawlerJobId |= decodedQuery.Contains("42", StringComparison.Ordinal);
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            uuid = 1784100864015L,
                            name = _packageName,
                            saveTo = "/downloads/staging/jdownloader"
                        }
                    }
                });
            }

            return JsonResponse(new { data = Array.Empty<object>() });
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CaptchaFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/downloadsV2/queryPackages")
            {
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            uuid = 1784100864015L,
                            name = "TeleJelly_test",
                            finished = true,
                            status = "An Error occurred!"
                        }
                    }
                });
            }

            return JsonResponse(new
            {
                data = new[]
                {
                    new
                    {
                        host = "linkcrawlerretry",
                        name = "Captcha unsupported! Unsupported captcha type 'powcaptcha.com'",
                        status = "File not found",
                        finished = false
                    }
                }
            });
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class AddContainerHandler : HttpMessageHandler
    {
        private bool _containerAdded;
        private bool _moved;

        public bool SubmittedAsJobAssignedDataUrl { get; private set; }
        public bool JobLinkFilterUsed { get; private set; }
        public bool DownloadLinkFilterUsed { get; private set; }
        public bool SetDirectoryCalled { get; private set; }
        public bool MoveToDownloadListCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var decodedQuery = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?'));
            switch (path)
            {
                case "/linkgrabberv2/addLinks":
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        var root = document.RootElement;
                        SubmittedAsJobAssignedDataUrl =
                            root.GetProperty("assignJobID").GetBoolean() &&
                            !root.GetProperty("autostart").GetBoolean() &&
                            root.GetProperty("dataURLs")[0].GetString() == "data:application/dlc;base64,AQID" &&
                            root.GetProperty("comment").GetString()!.StartsWith("TeleJelly:", StringComparison.Ordinal) &&
                            root.GetProperty("destinationFolder").GetString() == "/downloads/staging/jdownloader";
                    }

                    _containerAdded = true;
                    return JsonResponse(new { data = new { id = 42 } });
                case "/linkgrabberv2/queryLinkCrawlerJobs" when _containerAdded:
                    return JsonResponse(new
                    {
                        data = new[]
                        {
                            new { jobId = 42L, crawlerId = 43L, crawling = false, checking = false, crawled = 1, broken = 0, filtered = 0, unhandled = 0 }
                        }
                    });
                case "/linkgrabberv2/queryLinks" when _containerAdded && !_moved:
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        JobLinkFilterUsed = document.RootElement
                            .GetProperty("jobUUIDs")
                            .EnumerateArray()
                            .Any(id => id.GetInt64() == 42L);
                    }

                    return JsonResponse(new
                    {
                        data = new[]
                        {
                            new { uuid = 99L, jobUUID = 42L, packageUUID = 1784100864015L, name = "DLC link", enabled = true }
                        }
                    });
                case "/downloadsV2/queryLinks" when _moved:
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        DownloadLinkFilterUsed = document.RootElement
                            .GetProperty("linkUUIDs")
                            .EnumerateArray()
                            .Any(id => id.GetInt64() == 99L);
                    }

                    return JsonResponse(new
                    {
                        data = new[]
                        {
                            new { uuid = 99L, packageUUID = 1784100864999L, name = "DLC link", enabled = true }
                        }
                    });
                case "/linkgrabberv2/setDownloadDirectory":
                    var directoryParameters = decodedQuery.Split('&');
                    SetDirectoryCalled =
                        JsonSerializer.Deserialize<string>(directoryParameters[0]) == "/downloads/staging/jdownloader" &&
                        JsonSerializer.Deserialize<long[]>(directoryParameters[1])!.SequenceEqual([1784100864015L]);
                    return JsonResponse(new { data = true });
                case "/linkgrabberv2/moveToDownloadlist":
                    var moveParameters = decodedQuery.Split('&');
                    MoveToDownloadListCalled =
                        JsonSerializer.Deserialize<long[]>(moveParameters[0])!.SequenceEqual([99L]) &&
                        JsonSerializer.Deserialize<long[]>(moveParameters[1])!.Length == 0;
                    _moved = true;
                    return JsonResponse(new { data = true });
                default:
                    return JsonResponse(new { data = Array.Empty<object>() });
            }
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class OverlappingContainerHandler : HttpMessageHandler
    {
        private readonly HashSet<long> _movedLinkIds = [];
        private long _nextJobId = 100;

        public bool AllSubmissionsAssignedJobIds { get; private set; } = true;
        public List<long[]> MovedLinkBatches { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var decodedQuery = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?'));
            switch (path)
            {
                case "/linkgrabberv2/addLinks":
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        AllSubmissionsAssignedJobIds &=
                            document.RootElement.GetProperty("assignJobID").GetBoolean() &&
                            document.RootElement.GetProperty("dataURLs")[0].GetString()!.StartsWith("data:application/dlc;base64,", StringComparison.Ordinal);
                    }

                    return JsonResponse(new { data = new { id = ++_nextJobId } });
                case "/linkgrabberv2/queryLinkCrawlerJobs":
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        var jobId = document.RootElement.GetProperty("jobIds")[0].GetInt64();
                        return JsonResponse(new
                        {
                            data = new[]
                            {
                                new { jobId, crawlerId = jobId + 10, crawling = false, checking = false, crawled = 1, broken = 0, filtered = 0, unhandled = 0 }
                            }
                        });
                    }
                case "/linkgrabberv2/queryLinks":
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        var jobId = document.RootElement.GetProperty("jobUUIDs")[0].GetInt64();
                        var linkId = jobId == 101 ? 1001L : 1002L;
                        var linkGrabberPackageId = jobId == 101 ? 5001L : 5002L;
                        return JsonResponse(new
                        {
                            data = new[]
                            {
                                new { uuid = linkId, jobUUID = jobId, packageUUID = linkGrabberPackageId, name = $"DLC link {jobId}", enabled = true }
                            }
                        });
                    }
                case "/linkgrabberv2/setDownloadDirectory":
                    return JsonResponse(new { data = true });
                case "/linkgrabberv2/moveToDownloadlist":
                    var moveParameters = decodedQuery.Split('&');
                    var movedLinkIds = JsonSerializer.Deserialize<long[]>(moveParameters[0])!;
                    MovedLinkBatches.Add(movedLinkIds);
                    foreach (var linkId in movedLinkIds)
                    {
                        _movedLinkIds.Add(linkId);
                    }

                    return JsonResponse(new { data = true });
                case "/downloadsV2/queryLinks":
                    using (var document = JsonDocument.Parse(decodedQuery))
                    {
                        var requestedLinkIds = document.RootElement.GetProperty("linkUUIDs")
                            .EnumerateArray()
                            .Select(id => id.GetInt64())
                            .Where(_movedLinkIds.Contains)
                            .ToArray();
                        var links = requestedLinkIds
                            .Select(linkId => new
                            {
                                uuid = linkId,
                                packageUUID = linkId == 1001 ? 9001L : 9002L,
                                name = $"Download link {linkId}"
                            })
                            .ToArray();
                        return JsonResponse(new { data = links });
                    }
                default:
                    return JsonResponse(new { data = Array.Empty<object>() });
            }
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ExistingRejectedLinkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/linkgrabberv2/addLinks")
            {
                return JsonResponse(new { data = new { id = 1784127201844L } });
            }

            if (request.RequestUri.AbsolutePath == "/linkgrabberv2/queryLinks")
            {
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            host = "linkcrawlerretry",
                            name = "Captcha unsupported!Unsupported captcha type 'powcaptcha.com'",
                            packageUUID = 1784103783572L,
                            url = "https://filecrypt.cc/Container/70E431DA32.html",
                            finished = false
                        }
                    }
                });
            }

            return JsonResponse(new { data = Array.Empty<object>() });
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class LinkGrabberCaptchaFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/downloadsV2/queryPackages" => JsonResponse(new { data = Array.Empty<object>() }),
                "/linkgrabberv2/queryPackages" => JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            uuid = 1784103783572L,
                            name = "TeleJelly_test",
                            saveTo = "/downloads/staging/jdownloader"
                        }
                    }
                }),
                "/linkgrabberv2/queryLinks" => JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            host = "linkcrawlerretry",
                            name = "Captcha unsupported! Unsupported captcha type 'powcaptcha.com'",
                            finished = false
                        }
                    }
                }),
                _ => JsonResponse(new { data = Array.Empty<object>() })
            };
        }

        private static Task<HttpResponseMessage> JsonResponse(object value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }
}
