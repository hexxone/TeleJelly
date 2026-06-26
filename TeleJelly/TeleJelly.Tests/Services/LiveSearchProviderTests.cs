using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("LiveSearch")]
public class LiveSearchProviderTests
{
    [TestCaseSource(nameof(GetLiveProviders))]
    public async Task SearchAsync_LiveProvidersReturnWellFormedResultsWhenAvailable(LiveProviderCase providerCase)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var provider = providerCase.CreateProvider();

            var results = (await provider.SearchAsync(providerCase.Query, providerCase.ImdbId, cts.Token)).ToArray();

            Assert.That(results.Select(x => x.DownloadLink).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(results.Length));
            Assert.That(results.All(x => !string.IsNullOrWhiteSpace(x.Title)), Is.True);
            Assert.That(results.All(x => !string.IsNullOrWhiteSpace(x.DownloadLink)), Is.True);
        }
        catch (HttpRequestException ex)
        {
            Assert.Ignore($"Live provider request unavailable in current environment: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Assert.Ignore($"Live provider request timed out in current environment: {ex.Message}");
        }
    }

    public static IEnumerable<TestCaseData> GetLiveProviders()
    {
        yield return new TestCaseData(new LiveProviderCase("Funxd", () => new FunxdSearchProvider(new NullLogger<FunxdSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_Funxd_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("Jjs", () => new JjsSearchProvider(new NullLogger<JjsSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_Jjs_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("HdSource", () => new HdSourceSearchProvider(new NullLogger<HdSourceSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_HdSource_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("Filmfans", () => new FilmfansSearchProvider(new NullLogger<FilmfansSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_Filmfans_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("Serienfans", () => new SerienfansSearchProvider(new NullLogger<SerienfansSearchProvider>()), "House of Cards", null))
            .SetName("Live_Serienfans_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("DdlWarez", () => new DdlWarezSearchProvider(new NullLogger<DdlWarezSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_DdlWarez_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("Movieblog", () => new MovieblogSearchProvider(new NullLogger<MovieblogSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_Movieblog_SearchContract");
        yield return new TestCaseData(new LiveProviderCase("Hdencode", () => new HdencodeSearchProvider(new NullLogger<HdencodeSearchProvider>()), "The Matrix", "tt0133093"))
            .SetName("Live_Hdencode_SearchContract");
    }

    public sealed record LiveProviderCase(string Name, Func<Jellyfin.Plugin.TeleJelly.Services.Download.Search.ISearchProvider> CreateProvider, string Query, string? ImdbId);
}
