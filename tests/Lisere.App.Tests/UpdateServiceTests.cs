using System.Net;
using System.Net.Http;
using Lisere.Services;

namespace Lisere.App.Tests;

public class UpdateServiceTests
{
    private static readonly Uri VersionUri = new("https://example.test/version.txt");
    private static readonly Uri PackageUri = new("https://example.test/Lisere.zip");
    private static readonly Uri ChecksumUri = new("https://example.test/Lisere.zip.sha256");

    [Fact]
    public async Task ANewerPublishedVersionOffersAnUpdate()
    {
        var service = CreateService("1.2.0");

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Info);
        Assert.Equal("1.0.0", result.Info!.LocalVersion);
        Assert.Equal("1.2.0", result.Info.RemoteVersion);
        Assert.Equal(PackageUri, result.Info.PackageUri);
        Assert.Equal(ChecksumUri, result.Info.ChecksumUri);
    }

    [Fact]
    public async Task TheSameVersionIsUpToDate()
    {
        var service = CreateService("1.0.0");

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task AnUnreadableVersionIsReportedSeparately()
    {
        var service = CreateService("not-a-version");

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.InvalidVersion, result.Status);
        Assert.Null(result.Info);
    }

    [Fact]
    public async Task AnHttpFailureIsANetworkError()
    {
        var service = CreateService(statusCode: HttpStatusCode.NotFound);

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task ATransportFailureIsANetworkError()
    {
        var service = CreateService(exception: new HttpRequestException("offline"));

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task CancellationFromTheCallerIsNotConvertedToANetworkError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(exception: new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckForUpdatesAsync(cancellation.Token));
    }

    private static UpdateService CreateService(
        string? version = null,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Exception? exception = null) =>
        new(
            new HttpClient(new StubHandler(request =>
            {
                if (exception is not null)
                {
                    throw exception;
                }

                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(version ?? string.Empty)
                };
            })),
            currentVersion: "1.0.0",
            versionUri: VersionUri,
            packageUri: PackageUri,
            checksumUri: ChecksumUri);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
