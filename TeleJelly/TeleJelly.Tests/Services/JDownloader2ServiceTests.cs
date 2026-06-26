using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
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
}
