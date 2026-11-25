using ComputeSharp;
using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Enhancement;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Lisere Classic exécuté sur le GPU dédié. Le pipeline reste le même ; seul
/// le moteur de calcul change. L'étalonnage et la courbe du préréglage Chroma
/// passent par le même device, avant le readback. Si D3D12 n'est pas
/// disponible, si un adaptateur disparaît ou si le rendu échoue, la frame est
/// recalculée par le pipeline CPU, sans jamais rendre un fichier différent en
/// silence.
/// </summary>
public sealed class GpuClassicFrameEnhancer : IFrameEnhancer, IFrameStageTimingsProvider
{
    private readonly GpuClassicRenderer? _renderer;
    private readonly FrameLookEnhancer _cpuFallback;
    private readonly string? _fallbackReason;
    private readonly string? _providerNote;
    private string? _disabledReason;
    private bool _disabled;
    private bool _disposed;

    private GpuClassicFrameEnhancer(
        GpuClassicRenderer? renderer,
        string? fallbackReason,
        string? providerNote = null)
    {
        _renderer = renderer;
        _fallbackReason = fallbackReason;
        _providerNote = providerNote;
        _cpuFallback = new FrameLookEnhancer(new ClassicFrameEnhancer());
    }

    public string Id => "lisere.classic";

    public string DisplayName => "Lisere Classic";

    public EnhancementEngineFamily Family => EnhancementEngineFamily.Classical;

    public string Description =>
        "Pipeline déterministe six paramètres, amélioration, redimensionnement et étalonnage sur GPU D3D12 quand il est disponible.";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public int NativeScale => 1;

    public string? ActiveProvider =>
        _renderer is not null && !_disabled ? $"D3D12 · {_renderer.DeviceName}" : null;

    public string? ProviderFallbackReason =>
        _disabled ? _disabledReason : _fallbackReason;

    public string? ProviderNote => _disabled ? null : _providerNote;

    /// <summary>
    /// Crée le moteur GPU. <paramref name="enableGpu"/> est faux pour les tests
    /// de référence qui doivent garder le CPU.
    /// </summary>
    public static GpuClassicFrameEnhancer Create(bool enableGpu = true)
    {
        if (!enableGpu)
        {
            return new GpuClassicFrameEnhancer(null, "GPU désactivé");
        }

        if (!OperatingSystem.IsWindows())
        {
            return new GpuClassicFrameEnhancer(null, "Windows requis pour D3D12");
        }

        var device = GpuDeviceSelector.TryCreate(out var reason, out var note);
        if (device is null)
        {
            return new GpuClassicFrameEnhancer(null, reason);
        }

        try
        {
            var renderer = new GpuClassicRenderer(device);
            return new GpuClassicFrameEnhancer(renderer, null, note);
        }
        catch (Exception exception)
        {
            // Device partagé : on ne le libère pas, un autre moteur peut le tenir.
            return new GpuClassicFrameEnhancer(null, exception.Message);
        }
    }

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        // Le repli CPU n'a lieu que sans adaptateur ou après une panne du
        // device : le grain et l'étalonnage sont désormais appliqués par le
        // renderer, sur le GPU, donc un GPU disponible ne repasse jamais au
        // CPU.
        if (_renderer is null || _disabled)
        {
            return _cpuFallback.Enhance(source, request, cancellationToken);
        }

        try
        {
            return _renderer.Render(source, request, request.BufferPool, cancellationToken);
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

    public FrameStageTimings TakeStageTimings() =>
        _renderer?.TakeStageTimings() ?? FrameStageTimings.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderer?.Dispose();
        _cpuFallback.Dispose();
    }
}
