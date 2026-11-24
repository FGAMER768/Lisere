using ComputeSharp;
using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Lisere Classic exécuté sur le GPU dédié. Le pipeline reste le même ; seul
/// le moteur de calcul change. Si D3D12 n'est pas disponible, si un adaptateur
/// disparaît ou si le rendu échoue, la frame est recalculée par le pipeline
/// CPU, sans jamais rendre un fichier différent en silence.
/// </summary>
public sealed class GpuClassicFrameEnhancer : IFrameEnhancer
{
    private readonly GpuClassicRenderer? _renderer;
    private readonly GraphicsDevice? _device;
    private readonly FrameLookEnhancer _cpuFallback;
    private readonly string? _fallbackReason;
    private string? _disabledReason;
    private bool _disabled;
    private bool _disposed;

    private GpuClassicFrameEnhancer(
        GpuClassicRenderer? renderer,
        GraphicsDevice? device,
        string? fallbackReason)
    {
        _renderer = renderer;
        _device = device;
        _fallbackReason = fallbackReason;
        _cpuFallback = new FrameLookEnhancer(new ClassicFrameEnhancer());
    }

    public string Id => "lisere.classic";

    public string DisplayName => "Lisere Classic";

    public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

    public string Description =>
        "Pipeline déterministe six paramètres, amélioration et redimensionnement sur GPU D3D12 quand il est disponible.";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public int NativeScale => 1;

    public string? ActiveProvider =>
        _renderer is not null && !_disabled ? $"D3D12 · {_renderer.DeviceName}" : null;

    public string? ProviderFallbackReason =>
        _disabled ? _disabledReason : _fallbackReason;

    /// <summary>
    /// Crée le moteur GPU. <paramref name="enableGpu"/> est faux pour les tests
    /// de référence qui doivent garder le CPU.
    /// </summary>
    public static GpuClassicFrameEnhancer Create(bool enableGpu = true)
    {
        if (!enableGpu)
        {
            return new GpuClassicFrameEnhancer(null, null, "GPU désactivé");
        }

        if (!OperatingSystem.IsWindows())
        {
            return new GpuClassicFrameEnhancer(null, null, "Windows requis pour D3D12");
        }

        var device = GpuDeviceSelector.TryCreate(out var reason);
        if (device is null)
        {
            return new GpuClassicFrameEnhancer(null, null, reason);
        }

        try
        {
            var renderer = new GpuClassicRenderer(device);
            return new GpuClassicFrameEnhancer(renderer, device, null);
        }
        catch (Exception exception)
        {
            device.Dispose();
            return new GpuClassicFrameEnhancer(null, null, exception.Message);
        }
    }

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        // Le grain est encore calculé par le pipeline CPU : il intervient avant
        // le redimensionnement et la colorimétrie, et le déplacer après aurait
        // changé le rendu. Le chemin CPU reste donc exact pour ce réglage.
        if (_renderer is null ||
            _disabled ||
            request.AddNoise > EnhancementTuning.Noise.ActiveThreshold)
        {
            return _cpuFallback.Enhance(source, request, cancellationToken);
        }

        try
        {
            var output = _renderer.Render(source, request, cancellationToken);

            if (request.Grading is not ColorPreset.None)
            {
                output = ColorGrading.Apply(
                    output,
                    request.Grading,
                    request.GradingSettings);
            }

            return request.ApplyChromaTone
                ? ChromaTone.Apply(output)
                : output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Une perte de device ou une allocation refusée ne doit pas perdre
            // la frame : on recopie le résultat CPU et on n'utilise plus le GPU
            // pour cette session.
            _disabled = true;
            _disabledReason = exception.Message;
            _renderer.Dispose();
            return _cpuFallback.Enhance(source, request, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderer?.Dispose();
        _device?.Dispose();
        _cpuFallback.Dispose();
    }
}
