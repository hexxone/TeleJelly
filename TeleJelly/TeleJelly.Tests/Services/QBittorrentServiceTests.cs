using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Torrent;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TeleJelly.Tests.Infrastructure;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class QBittorrentServiceTests
{
    [Test]
    public async Task TestConnection_AcceptsNoContentLoginResponse()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                TorrentServices = new TorrentServicesSettings
                {
                    QBittorrent = new QBittorrentSettings
                    {
                        Enabled = true,
                        Host = "qbittorrent.test",
                        Port = 8080,
                        Username = "admin",
                        Password = "password"
                    }
                }
            }
        });
        using var service = new QBittorrentService(
            NullLogger<QBittorrentService>.Instance,
            new SuccessfulLoginHandler());

        var connected = await service.TestConnectionAsync(CancellationToken.None);

        Assert.That(connected, Is.True);
    }

    [Test]
    public async Task AddDownload_UploadsLocalTorrentAsMultipartContent()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                TorrentServices = new TorrentServicesSettings
                {
                    QBittorrent = new QBittorrentSettings
                    {
                        Enabled = true,
                        Host = "qbittorrent.test",
                        Port = 8080,
                        Username = "admin",
                        Password = "password",
                        StagingPath = "/downloads/staging/qbittorrent"
                    }
                }
            }
        });
        var torrentPath = Path.Combine(Path.GetTempPath(), $"telejelly-test-{Guid.NewGuid():N}.torrent");
        await File.WriteAllBytesAsync(torrentPath, [1, 2, 3, 4]);
        var handler = new TorrentUploadHandler();
        using var service = new QBittorrentService(NullLogger<QBittorrentService>.Instance, handler);

        var hash = await service.AddDownloadAsync(new Uri(torrentPath).AbsoluteUri, CancellationToken.None);

        Assert.That(hash, Is.EqualTo("abc123"));
        Assert.That(handler.AddContentType, Does.StartWith("multipart/form-data"));
        Assert.That(handler.AddBody, Does.Contain("application/x-bittorrent"));
        Assert.That(handler.AddBody, Does.Contain("telejelly-test-"));
        Assert.That(File.Exists(torrentPath), Is.False);
    }

    private sealed class SuccessfulLoginHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("5.1.2", Encoding.UTF8, "text/plain")
            });
        }
    }

    private sealed class TorrentUploadHandler : HttpMessageHandler
    {
        public string? AddContentType { get; private set; }
        public string? AddBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/add")
            {
                AddContentType = request.Content?.Headers.ContentType?.ToString();
                AddBody = request.Content == null
                    ? null
                    : Encoding.Latin1.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/info")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"hash\":\"abc123\",\"name\":\"test\",\"added_on\":1}]", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
