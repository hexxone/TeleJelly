using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Unit")]
public class PathTemplateServiceTests
{
    [Test]
    public async Task ResolvePathAsync_CombinesRelativePathWithLibraryRoot()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var libraryRoot = Path.Combine(Path.GetTempPath(), "telejelly", "library");
        var relativePath = Path.Combine("Movies", "Example (2024)");

        var result = await service.ResolvePathAsync(libraryRoot, relativePath);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(Path.Combine(libraryRoot, relativePath))));
    }

    [Test]
    public async Task ResolvePathAsync_PreservesAbsolutePath()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var libraryRoot = Path.Combine(Path.GetTempPath(), "telejelly", "library");
        var absolutePath = Path.Combine(Path.GetTempPath(), "telejelly", "custom", "Example (2024)");

        var result = await service.ResolvePathAsync(libraryRoot, absolutePath);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(absolutePath)));
    }

    [Test]
    public async Task ResolveTemplatePathAsync_AnchorsTemplateOutputToLibraryRoot()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var libraryRoot = Path.Combine(Path.GetTempPath(), "telejelly", "library");
        var download = new ManagedDownload
        {
            Title = "Example",
            Year = 2024,
            ImdbId = "tt1234567",
            Season = 1,
            Episode = 2
        };

        var result = await service.ResolveTemplatePathAsync(
            libraryRoot,
            "[Category]/{title} ({year})/S{season:00}E{episode:00}{ext}",
            download,
            new Dictionary<string, string> { ["Category"] = "Shows" },
            "Example.S01E02.mkv");

        var expected = Path.GetFullPath(Path.Combine(libraryRoot, "Shows", "Example (2024)", "S01E02.mkv"));
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExtractDynamicVariablesAsync_ReturnsConfiguredMovieSubcategorySelectors()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var library = new LibrarySettings
        {
            DynamicVariables =
            [
                new DynamicPathVariable
                {
                    Name = "Category",
                    Options = ["Marvel", "DC", "Anime"],
                    DefaultValue = "Marvel"
                },
                new DynamicPathVariable
                {
                    Name = "Edition",
                    Options = ["Theatrical", "Extended"]
                }
            ]
        };

        var variables = await service.ExtractDynamicVariablesAsync("[Category]/{title}/[Edition]/{filename}{ext}", library);

        Assert.That(variables.Select(x => x.Name), Is.EqualTo(new[] { "Category", "Edition" }));
        Assert.That(variables[0].Options, Is.EquivalentTo(new[] { "Marvel", "DC", "Anime" }));
        Assert.That(variables[0].DefaultValue, Is.EqualTo("Marvel"));
    }

    [Test]
    public async Task ApplyTemplateAsync_BuildsMoviePathWithSelectableSubcategory()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var download = new ManagedDownload
        {
            Title = "Spider-Man: Homecoming",
            Year = 2017,
            ImdbId = "tt2250912"
        };

        var result = await service.ApplyTemplateAsync(
            "[Category]/{title} ({year})/{filename}{ext}",
            download,
            new Dictionary<string, string> { ["Category"] = "Marvel" },
            "Spider-Man.Homecoming.2017.1080p.mkv");

        var expected = Path.Combine("Marvel", "Spider-Man: Homecoming (2017)", "Spider-Man.Homecoming.2017.1080p.mkv");
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task ApplyTemplateAsync_BuildsSeriesSeasonEpisodeLayout()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var download = new ManagedDownload
        {
            Title = "SKAM",
            Year = 2015,
            ImdbId = "tt5288312",
            Season = 4,
            Episode = 10
        };

        var result = await service.ApplyTemplateAsync(
            "[Category]/{title}/Season {season:00}/{title} - S{season:00}E{episode:00}{ext}",
            download,
            new Dictionary<string, string> { ["Category"] = "Drama" },
            "SKAM.S04E10.1080p.WEB.mkv");

        var expected = Path.Combine("Drama", "SKAM", "Season 04", "SKAM - S04E10.mkv");
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task ApplyTemplateAsync_RemovesUnusedDynamicSegmentsWhenNoSelectionExists()
    {
        var service = new PathTemplateService(new NullLogger<PathTemplateService>());
        var download = new ManagedDownload
        {
            Title = "Wake",
            Year = 2026,
            ImdbId = "tt9999999"
        };

        var result = await service.ApplyTemplateAsync(
            "[Category]/{title} ({year})/{filename}{ext}",
            download,
            new Dictionary<string, string>(),
            "Wake.2026.1080p.WEB.mkv");

        var expected = Path.Combine("Wake (2026)", "Wake.2026.1080p.WEB.mkv");
        Assert.That(result, Is.EqualTo(expected));
    }
}
