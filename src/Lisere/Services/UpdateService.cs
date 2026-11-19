using System.IO;
using System.Net.Http;

namespace Lisere.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NetworkError,
    InvalidVersion
}

public enum UpdateStage
{
    Downloading,
    Verifying,
    Extracting,
    Applying
}

public readonly record struct UpdateProgress(UpdateStage Stage, double Percent);

public sealed record UpdateInfo(
    string LocalVersion,
    string RemoteVersion,
    Uri PackageUri,
    Uri ChecksumUri);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info);

public interface IUpdateService
{
    string CurrentVersion { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadAndInstallAsync(
        UpdateInfo info,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Checks the published version file, then downloads the latest GitHub release
/// asset. The package is pinned by SHA-256 before it is allowed to replace any
/// application file.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string DefaultVersionUrl =
        "https://raw.githubusercontent.com/FGAMER768/Lisere/master/Update/version.txt";
    public const string DefaultPackageUrl =
        "https://github.com/FGAMER768/Lisere/releases/latest/download/Lisere.zip";
    public const string DefaultChecksumUrl =
        "https://github.com/FGAMER768/Lisere/releases/latest/download/Lisere.zip.sha256";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;
    private readonly Uri _versionUri;
    private readonly Uri _packageUri;
    private readonly Uri _checksumUri;

    public UpdateService(
        HttpClient? httpClient = null,
        string? currentVersion = null,
        Uri? versionUri = null,
        Uri? packageUri = null,
        Uri? checksumUri = null)
    {
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        CurrentVersion = currentVersion ?? UpdateVersion.Normalize(
            typeof(UpdateService).Assembly.GetName().Version);
        _versionUri = versionUri ?? new Uri(DefaultVersionUrl);
        _packageUri = packageUri ?? new Uri(DefaultPackageUrl);
        _checksumUri = checksumUri ?? new Uri(DefaultChecksumUrl);
    }

    public string CurrentVersion { get; }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            using var response = await _http.GetAsync(
                _versionUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!UpdateVersion.TryNormalize(text, out var remoteVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.InvalidVersion, null);
            }

            var info = new UpdateInfo(CurrentVersion, remoteVersion, _packageUri, _checksumUri);
            return new UpdateCheckResult(
                UpdateVersion.Compare(remoteVersion, CurrentVersion) > 0
                    ? UpdateCheckStatus.UpdateAvailable
                    : UpdateCheckStatus.UpToDate,
                info);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null);
        }
    }

    public async Task DownloadAndInstallAsync(
        UpdateInfo info,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var installDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        UpdateInstaller.EnsureWritable(installDirectory);

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"Lisere_Update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        var packagePath = Path.Combine(workDirectory, "Lisere.zip");
        var extractedDirectory = Path.Combine(workDirectory, "package");
        var launched = false;

        try
        {
            progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0));
            await DownloadPackageAsync(info.PackageUri, packagePath, progress, cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.Verifying, 78));
            var checksum = await ReadChecksumAsync(info.ChecksumUri, cancellationToken);
            await UpdatePackage.VerifyChecksumAsync(packagePath, checksum, cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.Extracting, 88));
            await Task.Run(
                () => UpdatePackage.ExtractSafely(packagePath, extractedDirectory),
                cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.Applying, 96));
            UpdateInstaller.Launch(
                extractedDirectory,
                installDirectory,
                Environment.ProcessId,
                workDirectory);
            launched = true;
            progress?.Report(new UpdateProgress(UpdateStage.Applying, 100));
        }
        finally
        {
            if (!launched)
            {
                TryDelete(workDirectory);
            }
        }
    }

    private async Task DownloadPackageAsync(
        Uri packageUri,
        string destination,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                packageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                if (total is > 0)
                {
                    // Reserve the last quarter of the bar for verification,
                    // extraction and the restart handoff.
                    progress?.Report(new UpdateProgress(
                        UpdateStage.Downloading,
                        Math.Min(75, received * 75d / total.Value)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                "errorUpdateDownload",
                "The update package could not be downloaded.",
                exception);
        }
    }

    private async Task<string> ReadChecksumAsync(
        Uri checksumUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                checksumUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                "errorUpdateChecksum",
                "The update checksum could not be downloaded.",
                exception);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is safe to remove on the next run.
        }
    }
}

public sealed class UpdateException : Exception, ILocalizedError
{
    public UpdateException(
        string messageKey,
        string message,
        Exception? innerException = null,
        params object?[] messageArguments)
        : base(message, innerException)
    {
        MessageKey = messageKey;
        MessageArguments = messageArguments;
    }

    public string MessageKey { get; }

    public IReadOnlyList<object?> MessageArguments { get; }
}

public static class UpdateVersion
{
    public static string Normalize(Version? version)
    {
        version ??= new Version(0, 0, 0);
        var build = Math.Max(version.Build, 0);
        var revision = Math.Max(version.Revision, 0);
        return revision > 0
            ? $"{version.Major}.{version.Minor}.{build}.{revision}"
            : $"{version.Major}.{version.Minor}.{build}";
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Version.TryParse(value.Trim(), out var version) ||
            version.Major < 0 ||
            version.Minor < 0)
        {
            return false;
        }

        normalized = Normalize(version);
        return true;
    }

    public static int Compare(string left, string right)
    {
        if (!TryParseForComparison(left, out var leftVersion) ||
            !TryParseForComparison(right, out var rightVersion))
        {
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        return leftVersion.CompareTo(rightVersion);
    }

    private static bool TryParseForComparison(string value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (!Version.TryParse(value, out var parsed))
        {
            return false;
        }

        version = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
    }
}
