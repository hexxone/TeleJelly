using System.Linq;
using Jellyfin.Plugin.TeleJelly.Services.Logging;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class TeleJellyLoggerProviderTests
{
    [Test]
    public void LoggerProvider_CapturesTeleJellyLogsOnly()
    {
        var store = new DownloadManagerLogStore();
        using var provider = new TeleJellyLoggerProvider(store);

        var downloadLogger = provider.CreateLogger("Jellyfin.Plugin.TeleJelly.Services.Download.DownloadOrchestrator");
        var healthLogger = provider.CreateLogger("Jellyfin.Plugin.TeleJelly.Services.Download.Health.ServiceHealthMonitor");
        var telegramLogger = provider.CreateLogger("Jellyfin.Plugin.TeleJelly.Telegram.TelegramBotService");
        var unrelatedLogger = provider.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host");

        downloadLogger.LogInformation("Started download workflow for {Title}", "Example");
        healthLogger.LogWarning("Transmission backend is degraded");
        telegramLogger.LogInformation("Telegram listener is ready");
        unrelatedLogger.LogInformation("This should not be mirrored");

        var entries = store.GetRecent().ToArray();

        Assert.That(entries, Has.Length.EqualTo(3));
        Assert.That(entries[0].Source, Is.EqualTo("Services.Download.DownloadOrchestrator"));
        Assert.That(entries[0].Message, Does.Contain("Started download workflow for Example"));
        Assert.That(entries[1].Source, Is.EqualTo("Services.Download.Health.ServiceHealthMonitor"));
        Assert.That(entries[1].Message, Does.Contain("Transmission backend is degraded"));
        Assert.That(entries[2].Source, Is.EqualTo("Telegram.TelegramBotService"));
        Assert.That(entries[2].Message, Does.Contain("Telegram listener is ready"));
    }

    [Test]
    public void LogStore_KeepsOnlyMostRecentEntries()
    {
        var store = new DownloadManagerLogStore();
        var writer = (IDownloadManagerLogWriter)store;

        for (var i = 0; i < 550; i++)
        {
            writer.Write(LogLevel.Information, "TestSource", $"Entry {i}");
        }

        var entries = store.GetRecent(500).ToArray();

        Assert.That(entries, Has.Length.EqualTo(500));
        Assert.That(entries[0].Message, Is.EqualTo("Entry 50"));
        Assert.That(entries[^1].Message, Is.EqualTo("Entry 549"));
    }
}
