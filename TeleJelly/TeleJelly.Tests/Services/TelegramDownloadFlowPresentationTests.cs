using System;
using System.Text;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Telegram;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class TelegramDownloadFlowPresentationTests
{
    [TestCase("library", DownloadStatus.AwaitingLibrary, true)]
    [TestCase("library", DownloadStatus.AwaitingMediaType, false)]
    [TestCase("result", DownloadStatus.AwaitingSearchResult, true)]
    [TestCase("result", DownloadStatus.AwaitingPathConfirm, false)]
    [TestCase("pathvar", DownloadStatus.AwaitingPathVars, true)]
    [TestCase("edittype", DownloadStatus.AwaitingPathConfirm, true)]
    [TestCase("edittype", DownloadStatus.AwaitingMediaType, false)]
    [TestCase("accept", DownloadStatus.AwaitingPathConfirm, true)]
    [TestCase("accept", DownloadStatus.Pending, false)]
    [TestCase("retry", DownloadStatus.ExtractionFailed, true)]
    [TestCase("cancel", DownloadStatus.Completed, false)]
    public void IsCallbackActionAllowed_RejectsButtonsFromStaleMenus(
        string action,
        DownloadStatus status,
        bool expected)
    {
        Assert.That(TelegramBotService.IsCallbackActionAllowed(action, status), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldAutoSelectSearchResult_OnlyWhenWinnerIsClearlyAhead()
    {
        var strongLead = DownloadFlowPresentation.ShouldAutoSelectSearchResult(
        [
            new SearchResult { Title = "Best", QualityScore = 1400 },
            new SearchResult { Title = "Second", QualityScore = 900 }
        ]);

        var closeRace = DownloadFlowPresentation.ShouldAutoSelectSearchResult(
        [
            new SearchResult { Title = "Best", QualityScore = 1200 },
            new SearchResult { Title = "Second", QualityScore = 1100 }
        ]);

        Assert.That(strongLead, Is.True);
        Assert.That(closeRace, Is.False);
    }

    [Test]
    public void ShouldAutoSelectSearchResult_NeverAutoSelectsQualityFallback()
    {
        var shouldSelect = DownloadFlowPresentation.ShouldAutoSelectSearchResult(
        [
            new SearchResult { Title = "Fallback", QualityScore = 1400, QualityFallback = true }
        ]);

        Assert.That(shouldSelect, Is.False);
    }

    [Test]
    public void BuildSearchResultLabel_IncludesKeyMetadata()
    {
        var label = DownloadFlowPresentation.BuildSearchResultLabel(new SearchResult
        {
            Title = "Fallback",
            Release = "Example.Release.2026",
            Resolution = "2160p",
            Source = "BluRay",
            FileSizeBytes = 5L * 1024 * 1024 * 1024,
            QualityScore = 1337
        });

        Assert.That(label, Does.Contain("Example.Release.2026"));
        Assert.That(label, Does.Contain("2160p"));
        Assert.That(label, Does.Contain("BluRay"));
        Assert.That(label, Does.Match(@"5[.,]0 GiB"));
        Assert.That(label, Does.Contain("S1337"));
    }

    [Test]
    public void BuildSearchResultLabel_MarksQualityFallback()
    {
        var label = DownloadFlowPresentation.BuildSearchResultLabel(new SearchResult
        {
            Title = "Fallback",
            QualityScore = 250,
            QualityFallback = true
        });

        Assert.That(label, Does.Contain("quality fallback"));
    }

    [Test]
    public void BuildSearchResultsMessage_IncludesDecisionMetadataAndEscapesHtml()
    {
        var message = DownloadFlowPresentation.BuildSearchResultsMessage("A <Movie>",
        [
            new SearchResult
            {
                Title = "Example <Release>",
                Provider = "provider.example",
                Resolution = "2160p",
                Source = "WEB-DL",
                Codec = "H.265",
                HDR = "Dolby Vision",
                FileSizeBytes = 8L * 1024 * 1024 * 1024,
                AudioLanguages = ["German", "English"],
                AudioCodecs = ["Atmos"],
                SubtitleLanguages = ["German"],
                Bitrate = 12500,
                QualityScore = 1400
            }
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("A &lt;Movie&gt;"));
            Assert.That(message, Does.Contain("Example &lt;Release&gt;"));
            Assert.That(message, Does.Contain("2160p · WEB-DL · H.265 · Dolby Vision · 12.5 Mbps"));
            Assert.That(message, Does.Contain("German, English, Atmos"));
            Assert.That(message, Does.Contain("8.0 GiB"));
            Assert.That(message, Does.Contain("Score 1400"));
        });
    }

    [Test]
    public void SelectAutomaticLibrary_UsesSingleCompatibleJellyfinType()
    {
        var movieLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Movies", "movies");
        var showLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Shows", "tvshows");
        var libraries = new[] { movieLibrary, showLibrary };

        Assert.Multiple(() =>
        {
            Assert.That(DownloadLibrarySelection.SelectAutomaticLibrary(libraries, MediaType.Movie), Is.EqualTo(movieLibrary));
            Assert.That(DownloadLibrarySelection.SelectAutomaticLibrary(libraries, MediaType.Series), Is.EqualTo(showLibrary));
        });
    }

    [Test]
    public void SelectAutomaticLibrary_UsesOnlyLibraryRegardlessOfType()
    {
        var onlyLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Movies", "movies");

        Assert.That(
            DownloadLibrarySelection.SelectAutomaticLibrary([onlyLibrary], MediaType.Series),
            Is.EqualTo(onlyLibrary));
    }

    [Test]
    public void GetSelectableLibraries_HidesIncompatibleTypesWhenCompatibleLibrariesExist()
    {
        var firstMovieLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Movies", "movies");
        var secondMovieLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Movies 4K", "movies");
        var showLibrary = new DownloadLibrarySelection.LibraryChoice(Guid.NewGuid(), "Shows", "tvshows");

        var selectable = DownloadLibrarySelection.GetSelectableLibraries(
            [firstMovieLibrary, secondMovieLibrary, showLibrary],
            MediaType.Movie);

        Assert.That(selectable, Is.EquivalentTo(new[] { firstMovieLibrary, secondMovieLibrary }));
    }

    [Test]
    public void TryParseDownloadCallback_ParsesActionAndValue()
    {
        var id = Guid.NewGuid();
        var parsed = DownloadFlowPresentation.TryParseDownloadCallback($"dl_{id}_result_3", out var downloadId, out var action, out var value);

        Assert.That(parsed, Is.True);
        Assert.That(downloadId, Is.EqualTo(id));
        Assert.That(action, Is.EqualTo("result"));
        Assert.That(value, Is.EqualTo("3"));
    }

    [Test]
    public void CreateLibraryCallbackData_StaysWithinTelegramLimitAndRoundTripsIds()
    {
        var downloadId = Guid.NewGuid();
        var libraryId = Guid.NewGuid();

        var callback = DownloadFlowPresentation.CreateLibraryCallbackData(downloadId, libraryId);
        var parsed = DownloadFlowPresentation.TryParseDownloadCallback(callback, out var parsedDownloadId, out var action, out var value);
        var parsedLibrary = DownloadFlowPresentation.TryParseLibraryCallbackValue(value, out var parsedLibraryId);

        Assert.That(Encoding.UTF8.GetByteCount(callback), Is.LessThanOrEqualTo(64));
        Assert.That(parsed, Is.True);
        Assert.That(parsedDownloadId, Is.EqualTo(downloadId));
        Assert.That(action, Is.EqualTo("library"));
        Assert.That(parsedLibrary, Is.True);
        Assert.That(parsedLibraryId, Is.EqualTo(libraryId));
    }

    [Test]
    public void TryParsePathVariableSelection_DecodesEncodedPayload()
    {
        var id = Guid.NewGuid();
        var callback = $"dl_{id}_pathvar_Season%20Name_Sci-Fi%20Collection";

        var parsed = DownloadFlowPresentation.TryParsePathVariableSelection(id, callback, out var name, out var value);

        Assert.That(parsed, Is.True);
        Assert.That(name, Is.EqualTo("Season Name"));
        Assert.That(value, Is.EqualTo("Sci-Fi Collection"));
    }

    [Test]
    public void FailureGuidance_IncludesManualSourceAndPassword()
    {
        var download = new ManagedDownload
        {
            ImdbId = "tt0080339",
            LinkOrMagnet = "https://example.org/container",
            SourcePassword = "secret"
        };

        var message = DownloadFailureGuidance.AppendReplyOption(
            DownloadFailureGuidance.Append(download, "Captcha unsupported."));

        Assert.That(message, Does.Contain("reply to this message with a URL, magnet link, `.torrent`, or `.dlc` file"));
        Assert.That(message, Does.Contain("`/download tt0080339 https://example.org/container`"));
        Assert.That(message, Does.Contain("`.torrent` or `.dlc` file with caption `/download tt0080339`"));
        Assert.That(message, Does.Contain("Source: https://example.org/container"));
        Assert.That(message, Does.Contain("Password: secret"));
    }

    [TestCase("/download tt0080339", "telejelly_bot", true)]
    [TestCase("/download@telejelly_bot tt0080339", "telejelly_bot", true)]
    [TestCase("/download@other_bot tt0080339", "telejelly_bot", false)]
    [TestCase("/download nope", "telejelly_bot", false)]
    public void TryParseDownloadFileCaption_ValidatesCommandAndImdbId(
        string caption,
        string botUsername,
        bool expected)
    {
        var parsed = DownloadFlowPresentation.TryParseDownloadFileCaption(caption, botUsername, out var imdbId);

        Assert.That(parsed, Is.EqualTo(expected));
        Assert.That(imdbId, Is.EqualTo(expected ? "tt0080339" : null));
    }

    [TestCase("https://example.org/file", true)]
    [TestCase("http://example.org/file", true)]
    [TestCase("magnet:?xt=urn:btih:1234", true)]
    [TestCase("ftp://example.org/file", false)]
    [TestCase("not a link", false)]
    public void TryParseManualDownloadSource_AcceptsSupportedSources(string text, bool expected)
    {
        var parsed = DownloadFlowPresentation.TryParseManualDownloadSource(text, out var source);

        Assert.That(parsed, Is.EqualTo(expected));
        Assert.That(source, Is.EqualTo(expected ? text : null));
    }
}
