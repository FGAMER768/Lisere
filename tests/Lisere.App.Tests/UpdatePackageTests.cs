using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Lisere.Services;

namespace Lisere.App.Tests;

public class UpdatePackageTests
{
    [Fact]
    public async Task AMatchingChecksumIsAccepted()
    {
        var path = CreatePackage(("Lisere.exe", "binary"));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        try
        {
            await UpdatePackage.VerifyChecksumAsync(
                path,
                $"{hash}  Lisere.zip",
                CancellationToken.None);
        }
        finally
        {
            DeletePackage(path);
        }
    }

    [Fact]
    public async Task AMismatchedChecksumIsRejected()
    {
        var path = CreatePackage(("Lisere.exe", "binary"));

        try
        {
            var exception = await Assert.ThrowsAsync<UpdateException>(
                () => UpdatePackage.VerifyChecksumAsync(
                    path,
                    new string('0', 64),
                    CancellationToken.None));

            Assert.Equal("errorUpdateChecksum", exception.MessageKey);
        }
        finally
        {
            DeletePackage(path);
        }
    }

    [Fact]
    public void AMalformedChecksumFileIsRejected()
    {
        var exception = Assert.Throws<UpdateException>(
            () => UpdatePackage.ParseChecksum("not a checksum"));

        Assert.Equal("errorUpdateChecksum", exception.MessageKey);
    }

    [Fact]
    public void ExtractionUnpacksTheApplicationFiles()
    {
        var path = CreatePackage(
            ("Lisere.exe", "binary"),
            ("ffmpeg/manifest.json", "{}"),
            ("ffmpeg/ffmpeg.exe", "ffmpeg"),
            ("ffmpeg/ffprobe.exe", "ffprobe"));
        var destination = Path.Combine(Path.GetTempPath(), $"lisere-extract-{Guid.NewGuid():N}");

        try
        {
            UpdatePackage.ExtractSafely(path, destination);

            Assert.True(File.Exists(Path.Combine(destination, "Lisere.exe")));
            Assert.True(File.Exists(Path.Combine(destination, "ffmpeg", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(destination, "ffmpeg", "ffmpeg.exe")));
        }
        finally
        {
            DeleteDirectory(destination);
            DeletePackage(path);
        }
    }

    [Fact]
    public void ExtractionRejectsTraversalEntries()
    {
        var path = CreatePackage(
            ("Lisere.exe", "binary"),
            ("../escaped.txt", "nope"));
        var destination = Path.Combine(Path.GetTempPath(), $"lisere-extract-{Guid.NewGuid():N}");

        try
        {
            var exception = Assert.Throws<UpdateException>(
                () => UpdatePackage.ExtractSafely(path, destination));

            Assert.Equal("errorUpdatePackage", exception.MessageKey);
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escaped.txt")));
        }
        finally
        {
            DeleteDirectory(destination);
            DeletePackage(path);
        }
    }

    [Fact]
    public void ExtractionRejectsAPackageWithoutTheApplication()
    {
        var path = CreatePackage(("readme.txt", "nothing to install"));
        var destination = Path.Combine(Path.GetTempPath(), $"lisere-extract-{Guid.NewGuid():N}");

        try
        {
            var exception = Assert.Throws<UpdateException>(
                () => UpdatePackage.ExtractSafely(path, destination));

            Assert.Equal("errorUpdatePackage", exception.MessageKey);
        }
        finally
        {
            DeleteDirectory(destination);
            DeletePackage(path);
        }
    }

    [Fact]
    public void ExtractionRejectsAPackageWithoutTheBundledFfmpeg()
    {
        var path = CreatePackage(("Lisere.exe", "binary"));
        var destination = Path.Combine(Path.GetTempPath(), $"lisere-extract-{Guid.NewGuid():N}");

        try
        {
            var exception = Assert.Throws<UpdateException>(
                () => UpdatePackage.ExtractSafely(path, destination));

            Assert.Equal("errorUpdatePackage", exception.MessageKey);
        }
        finally
        {
            DeleteDirectory(destination);
            DeletePackage(path);
        }
    }

    private static string CreatePackage(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lisere-package-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private static void DeletePackage(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
