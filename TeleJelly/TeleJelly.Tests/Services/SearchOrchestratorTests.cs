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

[Category("Component")]
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

    [Test]
    public async Task SearchAndRankAsync_FiltersOnlyKnownOversizedResults()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "Too large",
                    DownloadLink = "magnet:?xt=urn:btih:large",
                    Resolution = "2160p",
                    FileSizeBytes = 30L * 1024 * 1024 * 1024,
                    Seeders = 20,
                    ServiceType = DownloadServiceType.Torrent
                },
                new SearchResult
                {
                    Title = "Unknown size",
                    DownloadLink = "magnet:?xt=urn:btih:unknown",
                    Resolution = "1080p",
                    FileSizeBytes = 0,
                    Seeders = 10,
                    ServiceType = DownloadServiceType.Torrent
                }
            ]);

        var orchestrator = new SearchOrchestrator(
            [provider.Object],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>());

        var results = await orchestrator.SearchAndRankAsync(
            "query",
            null,
            new QualityProfile { MinimumSeeders = 1 },
            5,
            CancellationToken.None,
            ["provider"],
            10L * 1024 * 1024 * 1024);

        Assert.That(results.Select(r => r.Title), Is.EqualTo(new[] { "Unknown size" }));
    }

    [Test]
    public async Task SearchAndRankAsync_IsolatesProviderFailures()
    {
        var failingProvider = new Mock<ISearchProvider>();
        failingProvider.SetupGet(x => x.Name).Returns("broken");
        failingProvider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("boom"));

        var healthyProvider = new Mock<ISearchProvider>();
        healthyProvider.SetupGet(x => x.Name).Returns("healthy");
        healthyProvider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "Healthy result",
                    DownloadLink = "magnet:?xt=urn:btih:healthy",
                    Resolution = "1080p",
                    Seeders = 10,
                    ServiceType = DownloadServiceType.Torrent
                }
            ]);

        var orchestrator = new SearchOrchestrator(
            [failingProvider.Object, healthyProvider.Object],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>());

        var results = await orchestrator.SearchAndRankAsync("query", null, new QualityProfile { MinimumSeeders = 1 }, 10, CancellationToken.None);

        Assert.That(results.Select(x => x.Title), Is.EqualTo(new[] { "Healthy result" }));
    }

    [Test]
    public async Task SearchAndRankAsync_TruncatesToMaxResultsAfterRanking()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateResult("Top", "2160p", 50, uploadedDate: System.DateTime.UtcNow.AddDays(-1)),
                CreateResult("Second", "1080p", 20, uploadedDate: System.DateTime.UtcNow.AddDays(-2)),
                CreateResult("Third", "720p", 10, uploadedDate: System.DateTime.UtcNow.AddDays(-3))
            ]);

        var orchestrator = new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>());

        var results = await orchestrator.SearchAndRankAsync("query", null, new QualityProfile { MinimumSeeders = 1 }, 2, CancellationToken.None);

        Assert.That(results.Select(r => r.Title), Is.EqualTo(new[] { "Top", "Second" }));
    }

    [Test]
    public async Task SearchAndRankAsync_UsesAgeAwareRankingForComparableResults()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateResult("Newer", "1080p", 15, uploadedDate: System.DateTime.UtcNow.AddDays(-1)),
                CreateResult("Older", "1080p", 15, uploadedDate: System.DateTime.UtcNow.AddDays(-21), link: "magnet:?xt=urn:btih:older")
            ]);

        var orchestrator = new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>());

        var results = await orchestrator.SearchAndRankAsync("query", null, new QualityProfile { MinimumSeeders = 1 }, 5, CancellationToken.None);

        Assert.That(results.First().Title, Is.EqualTo("Newer"));
        Assert.That(results.First().QualityScore, Is.GreaterThan(results.Last().QualityScore));
    }

    private static SearchResult CreateResult(string title, string resolution, int seeders, System.DateTime? uploadedDate = null, string? link = null)
    {
        return new SearchResult
        {
            Title = title,
            DownloadLink = link ?? $"magnet:?xt=urn:btih:{title}",
            Resolution = resolution,
            Seeders = seeders,
            ServiceType = DownloadServiceType.Torrent,
            UploadedDate = uploadedDate
        };
    }
}
