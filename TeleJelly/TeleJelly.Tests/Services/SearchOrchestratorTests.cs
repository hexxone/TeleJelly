using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

public class SearchOrchestratorTests
{
    [Test]
    public async Task SearchAndRankAsync_RespectsEnabledProvidersAndOrdersByScore()
    {
        var providerA = new Mock<ISearchProvider>();
        providerA.SetupGet(x => x.Name).Returns("providerA");
        providerA.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "Low score",
                    DownloadLink = "magnet:?xt=urn:btih:1",
                    Resolution = "720p",
                    Seeders = 3,
                    ServiceType = DownloadServiceType.Torrent
                }
            ]);

        var providerB = new Mock<ISearchProvider>();
        providerB.SetupGet(x => x.Name).Returns("providerB");
        providerB.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "High score",
                    DownloadLink = "magnet:?xt=urn:btih:2",
                    Resolution = "2160p",
                    Seeders = 50,
                    ServiceType = DownloadServiceType.Torrent
                }
            ]);

        var orchestrator = new SearchOrchestrator(
            [providerA.Object, providerB.Object],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>());

        var profile = new QualityProfile
        {
            MinimumSeeders = 1
        };

        var results = await orchestrator.SearchAndRankAsync("query", null, profile, 5, CancellationToken.None, ["providerA", "providerB"]);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.First().Title, Is.EqualTo("High score"));
    }
}
