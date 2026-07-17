using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TeleJelly.Tests.Infrastructure;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class PyLoadServiceTests
{
    [Test]
    public void CanHandle_AcceptsMultilineHttpLinksOnly()
    {
        using var service = new PyLoadService(new NullLogger<PyLoadService>());

        var valid = service.CanHandle("https://example.org/a\nhttps://example.org/b");
        var invalid = service.CanHandle("https://example.org/a\nmagnet:?xt=urn:btih:test");

        Assert.That(valid, Is.True);
        Assert.That(invalid, Is.False);
    }

    [Test]
    public async Task ExtractPasswordFromDlcAsync_ReturnsEmbeddedPasswordWhenEnabled()
    {
        using var scope = new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                Extraction = new ExtractionSettings
                {
                    ExtractPasswordsFromDlc = true
                }
            }
        });

        const string xml = "<dlc><passwords>pyload-secret</passwords></dlc>";
        var payload = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)));
        using var service = new PyLoadService(new NullLogger<PyLoadService>());

        var password = await service.ExtractPasswordFromDlcAsync(payload, CancellationToken.None);

        Assert.That(password, Is.EqualTo("pyload-secret"));
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

        const string xml = "<dlc><passwords>pyload-secret</passwords></dlc>";
        var payload = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)));
        using var service = new PyLoadService(new NullLogger<PyLoadService>());

        var password = await service.ExtractPasswordFromDlcAsync(payload, CancellationToken.None);

        Assert.That(password, Is.Null);
    }
}
