using System.Diagnostics;
using ComputeSharp;
using Lisere.Ai.Enhancement.Gpu;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Applies the color grade shared by the image page and the video page. Grading
/// runs on the enhanced frame, exactly as <c>EnhancementOrchestrator</c> does for
/// a photo, so a neural model and the classical pipeline both produce the same
/// colors.
/// <para>
/// L'étalonnage tourne sur le GPU D3D12 quand il existe, sur le même adaptateur
/// que le rendu : la frame n'est plus notée par le CPU pendant que le GPU
/// attend. Sans adaptateur, ou si le device tombe, la référence CPU reprend la
/// frame et la raison est conservée pour la note du moteur.
/// </para>
/// </summary>
public sealed class FrameLookEnhancer : IFrameEnhancer, IFrameStageTimingsProvider
{
    private readonly IFrameEnhancer _inner;
    private readonly object _gate = new();
    private GpuLookRenderer? _look;
    private string? _lookNote;
    private bool _gpuChecked;
    private long _gradingTicks;
    private long _gpuLookTicks;
    private bool _disposed;

    public FrameLookEnhancer(IFrameEnhancer inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string Id => _inner.Id;

    public string DisplayName => _inner.DisplayName;

    public EnhancementEngineFamily Family => _inner.Family;

    public string Description => _inner.Description;

    public bool IsAvailable => _inner.IsAvailable;

    public string? UnavailableReason => _inner.UnavailableReason;

    public int NativeScale => _inner.NativeScale;

    public double SecondsPerMegapixel => _inner.SecondsPerMegapixel;

    public bool IsPassthrough => _inner.IsPassthrough;

    public string? ActiveProvider => _inner.ActiveProvider;

    public string? ProviderFallbackReason => _inner.ProviderFallbackReason;

    public EngineCalibrationKey? CalibrationKey => _inner.CalibrationKey;

    public EngineMeasurement? LastMeasurement => _inner.LastMeasurement;

    public bool IsVerified => _inner.IsVerified;

    /// <summary>
    /// La note du modèle, complétée par l'adaptateur d'étalonnage : « DirectML ·
    /// RTX 4070 · étalonnage D3D12 · RTX 4070 ».
    /// </summary>
    public string? ProviderNote
    {
        get
        {
            var inner = _inner.ProviderNote;
            var look = Volatile.Read(ref _lookNote);

            if (string.IsNullOrWhiteSpace(look))
            {
                return inner;
            }

            return string.IsNullOrWhiteSpace(inner) ? look : $"{inner} · {look}";
        }
    }

    /// <summary>
    /// An engine is shown to people: a list box or a screen reader must read the
    /// model's name, not the type of the wrapper.
    /// </summary>
    public override string ToString() => DisplayName;

    public RgbImage Enhance(
        RgbImage source,
        FrameEnhancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pool = request.BufferPool;
        var output = _inner.Enhance(source, request, cancellationToken);

        if (request.Grading is ColorPreset.None)
        {
            return output;
        }

        var look = EnsureGpu();

        if (look is not null)
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                var graded = look.Apply(
                    output,
                    request.Grading,
                    request.GradingSettings,
                    pool,
                    cancellationToken);
                // Le temps de l'étalonnage GPU est du calcul GPU : il rejoint
                // Compute pour que la trace ne l'appelle pas « CPU ».
                Interlocked.Add(ref _gpuLookTicks, Stopwatch.GetElapsedTime(started).Ticks);

                if (!ReferenceEquals(graded, output) && !ReferenceEquals(output, source))
                {
                    pool?.Return(output);
                }

                return graded;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Un device perdu ne doit pas perdre la frame : la référence
                // CPU la reprend et la note explique pourquoi.
                DisableGpu(exception.Message);
            }
        }

        return ApplyOnCpu(source, output, request, pool, cancellationToken);
    }

    private RgbImage ApplyOnCpu(
        RgbImage source,
        RgbImage output,
        FrameEnhancementRequest request,
        RgbImagePool? pool,
        CancellationToken cancellationToken)
    {
        if (request.Grading is not ColorPreset.None)
        {
            var started = Stopwatch.GetTimestamp();
            var graded = ColorGrading.Apply(
                output,
                request.Grading,
                request.GradingSettings,
                work: null,
                pool,
                cancellationToken);
            Interlocked.Add(ref _gradingTicks, Stopwatch.GetElapsedTime(started).Ticks);

            if (!ReferenceEquals(graded, output) && !ReferenceEquals(output, source))
            {
                pool?.Return(output);
            }

            output = graded;
        }

        return output;
    }

    private GpuLookRenderer? EnsureGpu()
    {
        lock (_gate)
        {
            if (_gpuChecked || _disposed)
            {
                return _look;
            }

            _gpuChecked = true;

            if (!OperatingSystem.IsWindows())
            {
                _lookNote = "étalonnage CPU (Windows requis pour D3D12)";
                return null;
            }

            var device = GpuDeviceSelector.TryCreate(out var reason, out var note);
            if (device is null)
            {
                _lookNote = $"étalonnage CPU ({reason})";
                return null;
            }

            try
            {
                _look = new GpuLookRenderer(device);
                _lookNote = $"étalonnage D3D12 · {device.Name}";
                if (!string.IsNullOrWhiteSpace(note))
                {
                    _lookNote = $"{_lookNote} · {note}";
                }

                return _look;
            }
            catch (Exception exception)
            {
                // Le device est partagé par tous les moteurs : on ne le libère
                // pas, on abandonne seulement le renderer de ce moteur.
                _lookNote = $"étalonnage CPU ({exception.Message})";
                return null;
            }
        }
    }

    private void DisableGpu(string reason)
    {
        lock (_gate)
        {
            _look?.Dispose();
            _look = null;
            _lookNote = $"étalonnage CPU ({reason})";
        }
    }

    public FrameStageTimings TakeStageTimings()
    {
        var timings = (_inner as IFrameStageTimingsProvider)?.TakeStageTimings()
            ?? FrameStageTimings.Empty;

        return timings with
        {
            Compute = timings.Compute + TimeSpan.FromTicks(Interlocked.Exchange(ref _gpuLookTicks, 0)),
            Grading = TimeSpan.FromTicks(Interlocked.Exchange(ref _gradingTicks, 0))
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _look?.Dispose();
            _look = null;
        }

        _inner.Dispose();
    }
}
