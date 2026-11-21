using System.IO;
using System.Linq;
using System.Windows;
using Lisere.Ai.Enhancement;
using Lisere.Ai.Interpolation;
using Lisere.Ai.Models;
using Lisere.Core.Imaging;
using Lisere.Services;
using Lisere.Video.Ffmpeg;
using Lisere.Video.Preview;
using Lisere.Video.Probing;
using Lisere.Video.Rendering;
using Lisere.ViewModels;
using Lisere.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lisere;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        var catalog = LoadModelCatalog();
        var toolchain = VideoToolchain.Locate();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILanguageStore, JsonLanguageStore>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<IDialogService>(
                    provider => provider.GetRequiredService<DialogService>());
                services.AddSingleton<IImageCodec, ImageCodec>();
                services.AddSingleton(
                    new EngineCalibrationStore(EngineCalibrationStore.DefaultPath));
                services.AddSingleton<IFaceDetector, LazyFaceDetector>();
                services.AddSingleton<IEnhancementOrchestrator, EnhancementOrchestrator>();
                services.AddSingleton<EnhanceViewModel>();
                services.AddSingleton<EnhancePage>();

                services.AddSingleton(toolchain);
                services.AddSingleton<IVideoProbe, FfprobeVideoProbe>();
                services.AddSingleton<IVideoFrameExtractor, VideoFrameExtractor>();
                services.AddSingleton<IVideoRenderService, VideoRenderService>();
                services.AddSingleton<IReadOnlyList<IFrameEnhancer>>(
                    _ => FrameEnhancerCatalog.Create(catalog));
                services.AddSingleton<IReadOnlyList<IFrameInterpolator>>(_ =>
                {
                    var rife = RifeFrameInterpolator.FromCatalog(catalog);
                    return rife is null
                        ? Array.Empty<IFrameInterpolator>()
                        : new[] { rife };
                });
                services.AddSingleton<VideoViewModel>();
                services.AddSingleton<VideoPage>();

                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<UpdateViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument => string.Equals(
                argument,
                "--uninstall",
                StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall();
            return;
        }

        _host.Start();
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        _host.Dispose();
        base.OnExit(e);
    }

    private void RunUninstall()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var localization = new LocalizationService(new JsonLanguageStore());
        var window = new UninstallWindow(localization);
        if (window.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        try
        {
            UninstallService.Launch(AppContext.BaseDirectory);
        }
        catch (UninstallException exception)
        {
            var message = exception.Error == UninstallError.CloseRunningApplication
                ? localization["uninstallRunning"]
                : localization["uninstallError"];
            MessageBox.Show(
                message,
                localization["uninstallWindowTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Shutdown();
    }

    private static ModelCatalog? LoadModelCatalog()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        if (directory is null)
        {
            return null;
        }

        try
        {
            return ModelCatalog.Load(directory);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A broken catalog must not stop the application: engines fall back
            // to the always-available classical pipeline.
            return null;
        }
    }
}
