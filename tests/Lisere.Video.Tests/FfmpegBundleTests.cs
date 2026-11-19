using System.Security.Cryptography;
using System.Text.Json;
using Lisere.Video.Ffmpeg;

namespace Lisere.Video.Tests;

public class FfmpegBundleTests
{
    [Fact]
    public void AValidBundleIsAcceptedAndExposesItsVersion()
    {
        using var bundle = new TemporaryBundle();
        bundle.WriteBinary("ffmpeg.exe", "ffmpeg");
        bundle.WriteBinary("ffprobe.exe", "ffprobe");
        bundle.WriteManifest("N-test");

        var result = FfmpegBundle.Verify(bundle.Directory);

        Assert.True(result.IsValid);
        Assert.Equal("N-test", result.Version);
        Assert.NotNull(result.Tools);
        Assert.Equal(
            Path.Combine(bundle.Directory, "ffmpeg.exe"),
            result.Tools!.FfmpegPath);
    }

    [Fact]
    public void ACorruptedBinaryIsRejected()
    {
        using var bundle = new TemporaryBundle();
        bundle.WriteBinary("ffmpeg.exe", "ffmpeg");
        bundle.WriteBinary("ffprobe.exe", "ffprobe");
        bundle.WriteManifest("N-test");
        File.WriteAllText(Path.Combine(bundle.Directory, "ffmpeg.exe"), "tampered");

        var result = FfmpegBundle.Verify(bundle.Directory);

        Assert.False(result.IsValid);
        Assert.Contains("ffmpeg.exe", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestEntryOutsideTheBundleIsRejected()
    {
        using var bundle = new TemporaryBundle();
        bundle.WriteBinary("ffmpeg.exe", "ffmpeg");
        bundle.WriteBinary("ffprobe.exe", "ffprobe");
        bundle.WriteManifest("N-test", extraName: "../outside.exe");

        var result = FfmpegBundle.Verify(bundle.Directory);

        Assert.False(result.IsValid);
        Assert.Contains("invalide", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingManifestIsReported()
    {
        using var bundle = new TemporaryBundle();

        var result = FfmpegBundle.Verify(bundle.Directory);

        Assert.False(result.IsValid);
        Assert.Contains("absent", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryBundle : IDisposable
    {
        public TemporaryBundle()
        {
            Directory = Path.Combine(
                Path.GetTempPath(),
                $"lisere-ffmpeg-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public void WriteBinary(string name, string content) =>
            File.WriteAllText(Path.Combine(Directory, name), content);

        public void WriteManifest(string version, string? extraName = null)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ffmpeg.exe"] = Hash("ffmpeg"),
                ["ffprobe.exe"] = Hash("ffprobe")
            };
            if (extraName is not null)
            {
                files[extraName] = new string('0', 64);
            }

            File.WriteAllText(
                Path.Combine(Directory, FfmpegBundle.ManifestFileName),
                JsonSerializer.Serialize(new
                {
                    version,
                    files
                }));
        }

        private string Hash(string content) =>
            Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
