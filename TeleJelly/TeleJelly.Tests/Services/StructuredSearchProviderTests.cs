using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Component")]
internal class StructuredSearchProviderTests
{
    [TestCaseSource(nameof(GetProviderFixtureCases))]
    public async Task SearchAsync_ParsesRealisticProviderFixtures(ProviderFixtureCase fixtureCase)
    {
        var fetcher = new FakeFetcher();
        fixtureCase.ConfigureFetcher(fetcher);

        var provider = fixtureCase.CreateProvider(fetcher);
        var results = (await provider.SearchAsync(fixtureCase.Query, fixtureCase.ImdbId, CancellationToken.None)).ToArray();

        fixtureCase.AssertResults(results);
    }

    public static IEnumerable<TestCaseData> GetProviderFixtureCases()
    {
        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Funxd",
                "Hamnet",
                "tt32627483",
                fetcher => new FunxdSearchProvider(new NullLogger<FunxdSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/search", StringComparison.Ordinal), _ => LoadFixture("Providers/funxd-search.json"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/posts/11", StringComparison.Ordinal), _ => LoadFixture("Providers/funxd-post.json"));
                },
                results =>
                {
                    var result = FindResult(results, "Hamnet");
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Title, Does.Contain("Hamnet"));
                        Assert.That(result.Codec, Is.EqualTo("H.265"));
                        Assert.That(result.Resolution, Is.EqualTo("1080p"));
                        Assert.That(result.Password, Is.EqualTo("funxd.site"));
                        Assert.That(result.AudioLanguages, Does.Contain("German"));
                        Assert.That(result.AudioLanguages, Does.Contain("English"));
                        Assert.That(result.FileSizeBytes, Is.GreaterThan(1500L * 1024 * 1024));
                    });
                }))
            .SetName("SearchAsync_Funxd_ParsesRealFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Jjs",
                "A Quiet Place Tag Eins",
                "tt13433802",
                fetcher => new JjsSearchProvider(new NullLogger<JjsSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/search", StringComparison.Ordinal), _ => LoadFixture("Providers/jjs-search.json"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/posts/21", StringComparison.Ordinal), _ => LoadFixture("Providers/jjs-post.json"));
                },
                results =>
                {
                    var result = FindResult(results, "A Quiet Place Tag Eins");
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Title, Does.Contain("A Quiet Place Tag Eins"));
                        Assert.That(result.Resolution, Is.EqualTo("1080P").IgnoreCase);
                        Assert.That(result.Codec, Is.EqualTo("H.264"));
                        Assert.That(result.Password, Is.EqualTo("jjs.page"));
                        Assert.That(result.SubtitleLanguages, Does.Contain("English"));
                        Assert.That(result.SubtitleLanguages, Does.Contain("Spanish"));
                        Assert.That(result.DownloadLink, Is.Not.Empty);
                    });
                }))
            .SetName("SearchAsync_Jjs_ParsesRealFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Movieblog",
                "Greenland 2",
                "tt14850054",
                fetcher => new MovieblogSearchProvider(new NullLogger<MovieblogSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/search", StringComparison.Ordinal), _ => LoadFixture("Providers/movieblog-search.json"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/posts/31", StringComparison.Ordinal), _ => LoadFixture("Providers/movieblog-post.json"));
                },
                results =>
                {
                    var result = FindResult(results, "Greenland.2.2026");
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Title, Does.Contain("Greenland.2.2026"));
                        Assert.That(result.Resolution, Is.EqualTo("2160p"));
                        Assert.That(result.Codec, Is.EqualTo("AV1"));
                        Assert.That(result.HDR, Is.EqualTo("Dolby Vision").Or.EqualTo("HDR"));
                        Assert.That(result.Source, Is.EqualTo("WEBRip"));
                        Assert.That(result.Password, Is.EqualTo("movieblog.to"));
                        Assert.That(result.AudioLanguages, Is.EquivalentTo(new[] { "German", "English" }));
                        Assert.That(result.FileSizeBytes, Is.GreaterThan(7L * 1024 * 1024 * 1024));
                    });
                }))
            .SetName("SearchAsync_Movieblog_ParsesRealFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "HdSource",
                "One Mile Chapter One",
                "tt0000001",
                fetcher => new HdSourceSearchProvider(new NullLogger<HdSourceSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.Query.Contains("One%20Mile", StringComparison.OrdinalIgnoreCase), _ => LoadFixture("Providers/hdsource-search.html"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/one-mile-chapter-one-2026", StringComparison.Ordinal), _ => LoadFixture("Providers/hdsource-post.html"));
                },
                results =>
                {
                    var result = FindResult(results, "One.Mile.Chapter.One.2026");
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Title, Does.Contain("One.Mile.Chapter.One.2026"));
                        Assert.That(result.Resolution, Is.EqualTo("1080p"));
                        Assert.That(result.Codec, Is.EqualTo("AV1"));
                        Assert.That(result.HDR, Is.EqualTo("HDR"));
                        Assert.That(result.Source, Is.EqualTo("WEBRip"));
                        Assert.That(result.Password, Is.EqualTo("hd-source.to"));
                        Assert.That(result.AudioLanguages, Is.EquivalentTo(new[] { "German", "English" }));
                    });
                }))
            .SetName("SearchAsync_HdSource_ParsesRealFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "DdlWarez",
                "Die Wiege der Hoelle",
                "tt13202618",
                fetcher => new DdlWarezSearchProvider(new NullLogger<DdlWarezSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/wp-json/wp/v2/search", StringComparison.Ordinal), _ => LoadFixture("Providers/ddl-warez-search.json"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/die-wiege-der-hoelle-2025", StringComparison.Ordinal), _ => LoadFixture("Providers/ddl-warez-post.html"));
                },
                results =>
                {
                    var result = FindResult(results, "Die.Wiege.der.Hoelle.2025");
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.Title, Does.Contain("Die.Wiege.der.Hoelle.2025"));
                        Assert.That(result.Resolution, Is.EqualTo("1080p"));
                        Assert.That(result.Codec, Is.EqualTo("H.264"));
                        Assert.That(result.Source, Is.EqualTo("BluRay"));
                        Assert.That(result.AudioLanguages, Is.EquivalentTo(new[] { "German", "English" }));
                        Assert.That(result.SubtitleLanguages, Is.EquivalentTo(new[] { "German" }));
                    });
                }))
            .SetName("SearchAsync_DdlWarez_ParsesRealisticFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Hdencode",
                "Protected Movie",
                null,
                fetcher => new HdencodeSearchProvider(new NullLogger<HdencodeSearchProvider>(), fetcher),
                fetcher =>
                {
                    fetcher.WhenGet(uri => uri.Query.Contains("Protected", StringComparison.OrdinalIgnoreCase), _ => LoadFixture("Providers/hdencode-search.html"));
                    fetcher.WhenGet(uri => uri.AbsolutePath.Contains("/protected-movie-2024", StringComparison.Ordinal), _ => LoadFixture("Providers/hdencode-locked.html"));
                    fetcher.WhenPost(uri => uri.AbsolutePath.Contains("/protected-movie-2024", StringComparison.Ordinal), form =>
                    {
                        Assert.That(form["content-protector-token"], Is.EqualTo("token-123"));
                        return LoadFixture("Providers/hdencode-unlocked.html");
                    });
                },
                results =>
                {
                    Assert.That(results, Is.Not.Null);
                }))
            .SetName("SearchAsync_Hdencode_UnlocksProtectedFixture");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Filmfans",
                "Curveball",
                null,
                fetcher => new FilmfansSearchProvider(new NullLogger<FilmfansSearchProvider>(), fetcher),
                fetcher => fetcher.WhenGet(_ => true, _ => LoadFixture("Providers/filmfans-search.html")),
                results =>
                {
                    Assert.That(results.Select(x => x.DownloadLink), Is.EquivalentTo(new[]
                    {
                        "https://filmfans.org/curveball",
                        "https://filmfans.org/dc-down"
                    }));
                    Assert.That(results.All(x => x.ServiceType == DownloadServiceType.Hosted), Is.True);
                }))
            .SetName("SearchAsync_Filmfans_UsesRealPageUrls");

        yield return new TestCaseData(
            new ProviderFixtureCase(
                "Serienfans",
                "SKAM",
                null,
                fetcher => new SerienfansSearchProvider(new NullLogger<SerienfansSearchProvider>(), fetcher),
                fetcher => fetcher.WhenGet(_ => true, _ => LoadFixture("Providers/serienfans-search.html")),
                results =>
                {
                    Assert.That(results.Select(x => x.DownloadLink), Is.EquivalentTo(new[]
                    {
                        "https://serienfans.org/skam",
                        "https://serienfans.org/the-institute",
                        "https://serienfans.org/untamed/1"
                    }));
                    Assert.That(results.All(x => x.ServiceType == DownloadServiceType.Hosted), Is.True);
                }))
            .SetName("SearchAsync_Serienfans_UsesRealPageUrls");
    }

    [Test]
    public void ExtractHdEncodeUnlockPayload_ParsesProtectedFormFixture()
    {
        var lockedHtml = LoadFixture("Providers/hdencode-locked.html");

        var payload = ConfigurableStructuredSearchProvider.ExtractHdEncodeUnlockPayload(lockedHtml);

        Assert.Multiple(() =>
        {
            Assert.That(payload["content-protector-token"], Is.EqualTo("token-123"));
            Assert.That(payload["content-protector-ident"], Is.EqualTo("ident-456"));
            Assert.That(payload["content-protector-submit"], Is.EqualTo("unlock"));
        });
    }

    [Test]
    public void HdencodeUnlockedFixture_ParsesProtectedMetadataSignals()
    {
        var unlockedHtml = LoadFixture("Providers/hdencode-unlocked.html");
        var title = ConfigurableStructuredSearchProvider.ExtractHtmlTitle(unlockedHtml);
        var decodedText = WebUtility.HtmlDecode(Regex.Replace(unlockedHtml, "<[^>]+>", " "));
        var combined = $"{title} {decodedText}";

        Assert.Multiple(() =>
        {
            Assert.That(title, Does.Contain("Protected Movie 2024"));
            Assert.That(ConfigurableStructuredSearchProvider.ExtractPassword(unlockedHtml, decodedText), Is.EqualTo("hd-pass"));
            Assert.That(ConfigurableStructuredSearchProvider.ParseCodec(combined), Is.EqualTo("H.264"));
            Assert.That(ConfigurableStructuredSearchProvider.ParseHdr(combined), Is.EqualTo("HDR"));
            Assert.That(ConfigurableStructuredSearchProvider.ParseSource(combined), Is.EqualTo("WEBRip"));
            Assert.That(ConfigurableStructuredSearchProvider.ExtractLanguages(decodedText, true), Does.Contain("English"));
            Assert.That(ConfigurableStructuredSearchProvider.ExtractLanguages(decodedText, false), Does.Contain("German"));
            Assert.That(ConfigurableStructuredSearchProvider.ParseEstimatedSizeBytes(decodedText, ["https://rapidgator.net/file/protected-movie-2024"]), Is.GreaterThan(2L * 1024 * 1024 * 1024));
        });
    }

    private static string LoadFixture(string relativePath)
    {
        var fullPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", relativePath);
        return File.ReadAllText(fullPath);
    }

    private static SearchResult FindResult(IEnumerable<SearchResult> results, string titleFragment)
    {
        var matches = results.Where(result => result.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.That(matches, Is.Not.Empty, $"Expected at least one result containing '{titleFragment}'.");
        return matches[0];
    }

    internal sealed record ProviderFixtureCase(
        string Name,
        string Query,
        string? ImdbId,
        Func<ISearchDocumentFetcher, ISearchProvider> CreateProvider,
        Action<FakeFetcher> ConfigureFetcher,
        Action<SearchResult[]> AssertResults);

    internal sealed class FakeFetcher : ISearchDocumentFetcher
    {
        private readonly List<(Func<Uri, bool> Match, Func<Uri, string> Response)> _getHandlers = [];
        private readonly List<(Func<Uri, bool> Match, Func<IReadOnlyDictionary<string, string>, string> Response)> _postHandlers = [];

        public void WhenGet(Func<Uri, bool> match, Func<Uri, string> response)
        {
            _getHandlers.Add((match, response));
        }

        public void WhenPost(Func<Uri, bool> match, Func<IReadOnlyDictionary<string, string>, string> response)
        {
            _postHandlers.Add((match, response));
        }

        public Task<string> GetStringAsync(Uri uri, CancellationToken ct)
        {
            var handler = _getHandlers.LastOrDefault(x => x.Match(uri));
            if (handler.Match == null)
            {
                throw new InvalidOperationException($"No GET handler configured for {uri}");
            }

            return Task.FromResult(handler.Response(uri));
        }

        public Task<string> PostFormAsync(Uri uri, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken ct)
        {
            var handler = _postHandlers.LastOrDefault(x => x.Match(uri));
            if (handler.Match == null)
            {
                throw new InvalidOperationException($"No POST handler configured for {uri}");
            }

            var payload = formValues.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            return Task.FromResult(handler.Response(payload));
        }
    }
}
