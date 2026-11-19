using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Lisere.Services;

/// <summary>
/// Validates and unpacks an update archive without trusting its entry names.
/// The package must contain Lisere.exe at its root and may not escape the
/// extraction directory or expand beyond a bounded size.
/// </summary>
public static class UpdatePackage
{
    private const long MaximumExpandedBytes = 1_500L * 1024 * 1024;
    private const int MaximumEntries = 5000;

    public static async Task VerifyChecksumAsync(
        string packagePath,
        string checksumFile,
        CancellationToken cancellationToken)
    {
        var expected = ParseChecksum(checksumFile);
        await using var stream = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(
                "errorUpdateChecksum",
                "The update package checksum does not match.");
        }
    }

    public static string ParseChecksum(string checksumFile)
    {
        var candidate = checksumFile
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (candidate is not { Length: 64 } || !candidate.All(Uri.IsHexDigit))
        {
            throw new UpdateException(
                "errorUpdateChecksum",
                "The update checksum file is invalid.");
        }

        return candidate;
    }

    public static void ExtractSafely(string packagePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var destinationPrefix = Path.TrimEndingDirectorySeparator(destinationRoot) +
            Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count > MaximumEntries)
            {
                throw InvalidPackage();
            }

            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw InvalidPackage();
                }

                var relativeName = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relativeName))
                {
                    continue;
                }

                if (Path.IsPathRooted(relativeName) ||
                    relativeName.Contains(':'))
                {
                    throw InvalidPackage();
                }

                var destination = Path.GetFullPath(
                    Path.Combine(destinationRoot, relativeName));
                if (!destination.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidPackage();
                }

                if (entry.FullName.EndsWith('/') ||
                    entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or OverflowException or ArgumentException or
            NotSupportedException)
        {
            throw new UpdateException(
                "errorUpdatePackage",
                "The update package could not be extracted.",
                exception);
        }

        string[] requiredFiles =
        [
            "Lisere.exe",
            Path.Combine("ffmpeg", "manifest.json"),
            Path.Combine("ffmpeg", "ffmpeg.exe"),
            Path.Combine("ffmpeg", "ffprobe.exe")
        ];
        if (requiredFiles.Any(file =>
                !File.Exists(Path.Combine(destinationRoot, file))))
        {
            throw new UpdateException(
                "errorUpdatePackage",
                "The update package is incomplete.");
        }
    }

    private static UpdateException InvalidPackage() =>
        new("errorUpdatePackage", "The update package contains an invalid path.");
}
