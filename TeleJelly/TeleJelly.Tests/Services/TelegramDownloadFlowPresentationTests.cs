using System;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Telegram;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class TelegramDownloadFlowPresentationTests
{
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
    public void TryParsePathVariableSelection_DecodesEncodedPayload()
    {
        var id = Guid.NewGuid();
        var callback = $"dl_{id}_pathvar_Season%20Name_Sci-Fi%20Collection";

        var parsed = DownloadFlowPresentation.TryParsePathVariableSelection(id, callback, out var name, out var value);

        Assert.That(parsed, Is.True);
        Assert.That(name, Is.EqualTo("Season Name"));
        Assert.That(value, Is.EqualTo("Sci-Fi Collection"));
    }
}
