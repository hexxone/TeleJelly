using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TeleJelly.Tests.Infrastructure;

namespace TeleJelly.Tests.Services;

[Category("Component")]
public class ArchiveExtractionServiceTests
{
    [Test]
    public async Task DetectArchivesAsync_ReturnsOnlyFirstFilesForMultipartSets()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "movie.part01.rar"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir.FullName, "movie.part02.rar"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir.FullName, "show.rar"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir.FullName, "show.r00"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir.FullName, "show.r01"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir.FullName, "archive.zip"), string.Empty);

            var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());

            var archives = await service.DetectArchivesAsync(tempDir.FullName);
            var names = archives.Select(file => file.Name).ToArray();

            Assert.That(names, Does.Contain("movie.part01.rar"));
            Assert.That(names, Does.Contain("show.rar"));
            Assert.That(names, Does.Contain("archive.zip"));
            Assert.That(names, Does.Not.Contain("movie.part02.rar"));
            Assert.That(names, Does.Not.Contain("show.r00"));
            Assert.That(names, Does.Not.Contain("show.r01"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public async Task TryAllPasswordsAsync_ReturnsUsablePasswordForUnprotectedZip()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(tempDir.FullName, "source"));
            File.WriteAllText(Path.Combine(sourceDir.FullName, "sample.txt"), "hello");

            var zipPath = Path.Combine(tempDir.FullName, "sample.zip");
            ZipFile.CreateFromDirectory(sourceDir.FullName, zipPath);

            var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());

            var password = await service.TryAllPasswordsAsync(zipPath, ["wrong-password"], CancellationToken.None);
            var contents = await service.GetArchiveContentsAsync(zipPath, password);

            Assert.That(contents, Has.Length.EqualTo(1));
            Assert.That(contents[0], Is.EqualTo("sample.txt"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public async Task TryAllPasswordsAsync_ReturnsConfiguredPasswordForProtectedArchiveFixture()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var archivePath = GetFixturePath("Archives/protected-single.7z");

        var password = await service.TryAllPasswordsAsync(archivePath, ["wrong-password", "telejelly"], CancellationToken.None);
        var contents = await service.GetArchiveContentsAsync(archivePath, password);

        Assert.That(password, Is.EqualTo("telejelly"));
        Assert.That(contents, Has.Member("source/secret.txt"));
    }

    [Test]
    public async Task ExtractArchiveAsync_ExtractsProtectedSingleArchiveFixture()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var destination = Directory.CreateTempSubdirectory();

        try
        {
            await service.ExtractArchiveAsync(
                GetFixturePath("Archives/protected-single.7z"),
                destination.FullName,
                ["wrong-password", "telejelly"],
                new Progress<int>(),
                CancellationToken.None);

            var extractedFile = Path.Combine(destination.FullName, "source", "secret.txt");
            Assert.That(File.Exists(extractedFile), Is.True);
            Assert.That(await File.ReadAllTextAsync(extractedFile), Does.Contain("protected single archive"));
        }
        finally
        {
            destination.Delete(true);
        }
    }

    [Test]
    public async Task ExtractArchiveAsync_ExtractsPlainSingleArchiveFixture()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var source = Directory.CreateTempSubdirectory();
        var destination = Directory.CreateTempSubdirectory();
        var archivePath = Path.Combine(Path.GetTempPath(), $"plain-single-{Guid.NewGuid():N}.zip");

        try
        {
            File.WriteAllText(Path.Combine(source.FullName, "plain-single.txt"), "plain single archive");
            ZipFile.CreateFromDirectory(source.FullName, archivePath);
            await service.ExtractArchiveAsync(
                archivePath,
                destination.FullName,
                ["wrong-password"],
                new Progress<int>(),
                CancellationToken.None);

            var extractedFile = Path.Combine(destination.FullName, "plain-single.txt");
            Assert.That(File.Exists(extractedFile), Is.True);
            Assert.That(await File.ReadAllTextAsync(extractedFile), Does.Contain("plain single archive"));
        }
        finally
        {
            source.Delete(true);
            destination.Delete(true);
            File.Delete(archivePath);
        }
    }

    [Test]
    public async Task ExtractArchiveAsync_ExtractsUnprotectedMultipartArchiveFixture()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var destination = Directory.CreateTempSubdirectory();

        try
        {
            await service.ExtractArchiveAsync(
                GetFixturePath("Archives/multipart-plain.7z.001"),
                destination.FullName,
                ["wrong-password"],
                new Progress<int>(),
                CancellationToken.None);

            var extractedFile = Path.Combine(destination.FullName, "source", "multipart-plain.txt");
            Assert.That(File.Exists(extractedFile), Is.True);
            Assert.That(new FileInfo(extractedFile).Length, Is.GreaterThan(2048));
        }
        finally
        {
            destination.Delete(true);
        }
    }

    [Test]
    public async Task ExtractArchiveAsync_ExtractsProtectedMultipartArchiveFixture()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var destination = Directory.CreateTempSubdirectory();

        try
        {
            await service.ExtractArchiveAsync(
                GetFixturePath("Archives/multipart-protected.7z.001"),
                destination.FullName,
                ["telejelly", "wrong-password"],
                new Progress<int>(),
                CancellationToken.None);

            var extractedFile = Path.Combine(destination.FullName, "source", "multipart-protected.txt");
            Assert.That(File.Exists(extractedFile), Is.True);
            Assert.That(new FileInfo(extractedFile).Length, Is.GreaterThan(2048));
        }
        finally
        {
            destination.Delete(true);
        }
    }

    [Test]
    public async Task DetectArchivesAsync_ReturnsMultipleSeparateArchiveFamiliesFromFixtureDirectory()
    {
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());

        var archives = await service.DetectArchivesAsync(GetFixturePath("Archives/mixed-sets"));
        var names = archives.Select(file => file.Name).OrderBy(x => x).ToArray();

        Assert.That(names, Is.EqualTo(new[]
        {
            "mixed-a.7z.001",
            "mixed-b.7z.001"
        }));
    }

    [Test]
    public async Task ExtractArchiveAsync_HandlesEachMultipartSeriesInSharedFixtureDirectory()
    {
        using var scope = CreateExtractionScope();
        var service = new ArchiveExtractionService(new NullLogger<ArchiveExtractionService>());
        var sourceDirectory = GetFixturePath("Archives/mixed-sets");
        var archives = await service.DetectArchivesAsync(sourceDirectory);
        var destination = Directory.CreateTempSubdirectory();

        try
        {
            foreach (var archive in archives)
            {
                var target = Path.Combine(destination.FullName, Path.GetFileNameWithoutExtension(archive.Name));
                await service.ExtractArchiveAsync(archive.FullName, target, ["telejelly"], new Progress<int>(), CancellationToken.None);
            }

            Assert.That(File.Exists(Path.Combine(destination.FullName, "mixed-a.7z", "source", "mixed-a.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination.FullName, "mixed-b.7z", "source", "mixed-b.txt")), Is.True);
        }
        finally
        {
            destination.Delete(true);
        }
    }

    private static TestPluginScope CreateExtractionScope()
    {
        return new TestPluginScope(new PluginConfiguration
        {
            DownloadManager = new DownloadManagerSettings
            {
                Extraction = new ExtractionSettings
                {
                    Passwords = ["telejelly"],
                    DeleteArchivesAfterExtraction = false,
                    RecursiveExtractionDepth = 0,
                    FreeSpaceMarginPercent = 0
                }
            }
        });
    }

    private static string GetFixturePath(string relativePath)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", relativePath);
    }
}
