using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
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
    public async Task SearchAndRankAsync_FiltersBrokenFileCryptContainersBeforeRanking()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateResult("Broken", "1080p", 10, link: "https://filecrypt.cc/Container/BROKEN.html")]);
        var validator = new Mock<IDownloadLinkValidator>();
        validator.Setup(x => x.CanValidate(It.IsAny<string>())).Returns(true);
        validator.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadLinkValidationStatus.Broken);
        var progress = new SearchProgress();
        var orchestrator = new SearchOrchestrator(
            [provider.Object],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>(),
            validator.Object);

        var results = await orchestrator.SearchAndRankAsync(
            "Broken 2026",
            null,
            new QualityProfile { MinimumSeeders = 1 },
            5,
            CancellationToken.None,
            progress: progress);

        Assert.That(results, Is.Empty);
        var snapshot = progress.GetSnapshot();
        Assert.That(snapshot.CompletedProviders, Is.EqualTo(1));
        Assert.That(snapshot.CompletedWorkUnits, Is.EqualTo(2));
        Assert.That(snapshot.TotalWorkUnits, Is.EqualTo(2));
    }

    [Test]
    public async Task SearchAndRankAsync_StartsEnabledProvidersInParallel()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerA = new CoordinatedProvider("providerA", release.Task);
        var providerB = new CoordinatedProvider("providerB", release.Task);
        var orchestrator = new SearchOrchestrator(
            [providerA, providerB],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>());

        var searchTask = orchestrator.SearchAndRankAsync(
            "query",
            null,
            new QualityProfile(),
            5,
            CancellationToken.None);

        await Task.WhenAll(providerA.Started, providerB.Started).WaitAsync(System.TimeSpan.FromSeconds(2));
        release.SetResult();
        await searchTask;

        Assert.That(providerA.SearchCount, Is.EqualTo(1));
        Assert.That(providerB.SearchCount, Is.EqualTo(1));
    }

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
    public async Task SearchAndRankAsync_EmptyEnabledProvidersRunsEveryProvider()
    {
        var providerA = new Mock<ISearchProvider>();
        providerA.SetupGet(x => x.Name).Returns("providerA");
        providerA.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var providerB = new Mock<ISearchProvider>();
        providerB.SetupGet(x => x.Name).Returns("providerB");
        providerB.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var orchestrator = new SearchOrchestrator(
            [providerA.Object, providerB.Object],
            new QualityRuleEngine(),
            Mock.Of<ILogger<SearchOrchestrator>>());

        await orchestrator.SearchAndRankAsync(
            "query",
            null,
            new QualityProfile(),
            5,
            CancellationToken.None,
            []);

        providerA.Verify(x => x.SearchAsync("query", null, It.IsAny<CancellationToken>()), Times.Once);
        providerB.Verify(x => x.SearchAsync("query", null, It.IsAny<CancellationToken>()), Times.Once);
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

    [Test]
    public async Task SearchAndRankAsync_ReturnsQualityFallbacksWhenEveryResultFailsStrictRules()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Title = "Only size violation",
                    DownloadLink = "https://host.example/size",
                    Resolution = "1080p",
                    FileSizeBytes = 1L * 1024 * 1024 * 1024,
                    AudioLanguages = ["German"],
                    ServiceType = DownloadServiceType.Hosted
                },
                new SearchResult
                {
                    Title = "Size and language violation",
                    DownloadLink = "https://host.example/bad",
                    Resolution = "1080p",
                    FileSizeBytes = 1L * 1024 * 1024 * 1024,
                    AudioLanguages = ["French"],
                    ServiceType = DownloadServiceType.Hosted
                }
            ]);

        var profile = new QualityProfile
        {
            MinimumSeeders = 1,
            RequiredAudioLanguages = ["ger"],
            RequiredSubtitleLanguages = [],
            MinFileSizeByResolution = [new ResolutionSettings { Resolution = "1080p", Bytes = 5L * 1024 * 1024 * 1024 }]
        };

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync("Example 2026", null, profile, 5, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(result => result.QualityFallback), Is.True);
        Assert.That(results.First().Title, Is.EqualTo("Only size violation"));
    }

    [Test]
    public async Task SearchAndRankAsync_DoesNotMixQualityFallbacksWithStrictResults()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        var strict = CreateResult("Strict", "1080p", 10);
        strict.FileSizeBytes = 8L * 1024 * 1024 * 1024;
        var tooSmall = CreateResult("Too small", "1080p", 10, link: "magnet:?xt=urn:btih:small");
        tooSmall.FileSizeBytes = 1L * 1024 * 1024 * 1024;
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                strict,
                tooSmall
            ]);

        var profile = new QualityProfile
        {
            MinimumSeeders = 1,
            RequiredAudioLanguages = [],
            RequiredSubtitleLanguages = [],
            MinFileSizeByResolution = [new ResolutionSettings { Resolution = "1080p", Bytes = 5L * 1024 * 1024 * 1024 }]
        };

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync("Example 2026", null, profile, 5, CancellationToken.None);

        Assert.That(results.Select(result => result.Title), Is.EqualTo(new[] { "Strict" }));
        Assert.That(results.Single().QualityFallback, Is.False);
    }

    [Test]
    public async Task SearchAndRankAsync_RejectsConflictingYearMetadata()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateResult("Airplane II 1982", "1080p", 10)]);

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync("Airplane! 1980", "tt0080339", new QualityProfile { MinimumSeeders = 1 }, 5, CancellationToken.None);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task SearchAndRankAsync_RejectsSameYearResultWithUnrelatedTitle()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateResult("Agentenpoker (1980)", "1080p", 10)]);

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync(
                "Airplane! 1980",
                "tt0080339",
                new QualityProfile { MinimumSeeders = 1 },
                5,
                CancellationToken.None,
                titleAliases: ["Airplane!", "Die unglaubliche Reise in einem verrückten Flugzeug"]);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task SearchAndRankAsync_AcceptsAlternativeTitleAndSearchesForIt()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string searchQuery, string? unusedImdbId, CancellationToken unusedCancellationToken) => Task.FromResult<IEnumerable<SearchResult>>(
                searchQuery.StartsWith("Die unglaubliche Reise", System.StringComparison.Ordinal)
                    ? [CreateResult("Die.unglaubliche.Reise.in.einem.verrueckten.Flugzeug.1980.German.DL.1080p.BluRay.x264", "1080p", 10)]
                    : []));

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync(
                "Airplane! 1980",
                "tt0080339",
                new QualityProfile { MinimumSeeders = 1 },
                5,
                CancellationToken.None,
                titleAliases: ["Airplane!", "Die unglaubliche Reise in einem verrückten Flugzeug"]);

        Assert.That(results, Has.Count.EqualTo(1));
        provider.Verify(
            x => x.SearchAsync("Die unglaubliche Reise in einem verrückten Flugzeug 1980", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void SearchTitleMatcher_NormalizesPunctuationAndDiacritics()
    {
        var result = CreateResult("Die.unglaubliche.Reise.in.einem.verrueckten.Flugzeug.1980", "1080p", 10);

        Assert.That(
            SearchTitleMatcher.IsMatch(result, ["Die unglaubliche Reise in einem verrückten Flugzeug"], "tt0080339"),
            Is.True);
    }

    [Test]
    public async Task SearchAndRankAsync_TreatsPaddedAndUnpaddedSeasonNumbersAsEqual()
    {
        var provider = new Mock<ISearchProvider>();
        provider.SetupGet(x => x.Name).Returns("provider");
        provider.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateResult("Example 2026 S1", "1080p", 10)]);

        var results = await new SearchOrchestrator([provider.Object], new QualityRuleEngine(), Mock.Of<ILogger<SearchOrchestrator>>())
            .SearchAndRankAsync("Example 2026 S01", null, new QualityProfile { MinimumSeeders = 1 }, 5, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(1));
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

    private sealed class CoordinatedProvider(string name, Task release) : ISearchProvider
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public Task Started => _started.Task;

        public int SearchCount { get; private set; }

        public async Task<IEnumerable<SearchResult>> SearchAsync(string query, string? imdbId, CancellationToken ct)
        {
            SearchCount++;
            _started.TrySetResult();
            await release.WaitAsync(ct);
            return [];
        }
    }
}
