using System.Diagnostics;

namespace Lisere.Core.Imaging;

/// <summary>
/// Cooperative cancellation and row-level progress shared by the imaging
/// passes. A slice maps its own 0..1 fraction onto the parent range, so nested
/// passes can report without knowing the overall percentage.
/// </summary>
public sealed class ImagingWork
{
    public const int DefaultBandRows = 64;

    private readonly CancellationToken _cancellation;
    private readonly Action<double>? _progress;
    private readonly double _from;
    private readonly double _to;
    private readonly int _bandRows;
    private readonly ReportThrottle _throttle;
    private bool _completed;

    public ImagingWork(
        CancellationToken cancellationToken,
        Action<double>? progress = null,
        int bandRows = DefaultBandRows)
        : this(cancellationToken, progress, 0.0, 1.0, bandRows, new ReportThrottle())
    {
    }

    private ImagingWork(
        CancellationToken cancellationToken,
        Action<double>? progress,
        double from,
        double to,
        int bandRows,
        ReportThrottle throttle)
    {
        _cancellation = cancellationToken;
        _progress = progress;
        _from = from;
        _to = to;
        _bandRows = Math.Max(1, bandRows);
        _throttle = throttle;
    }

    private bool IsActive => _progress is not null || _cancellation.CanBeCanceled;

    public ImagingWork Slice(double from, double to) =>
        new(
            _cancellation,
            _progress,
            _from + ((_to - _from) * Math.Clamp(from, 0.0, 1.0)),
            _from + ((_to - _from) * Math.Clamp(to, 0.0, 1.0)),
            _bandRows,
            _throttle);

    /// <summary>
    /// Called at the top of a row loop. Checks the token and reports progress
    /// once per band; the modulo keeps the cost negligible next to a pass.
    /// </summary>
    public void Report(int row, int height)
    {
        if (_completed || !IsActive || row % _bandRows != 0)
        {
            return;
        }

        _cancellation.ThrowIfCancellationRequested();

        if (_progress is not null && height > 0 && _throttle.ShouldReport())
        {
            var fraction = Math.Clamp(row / (double)height, 0.0, 1.0);
            _progress(_from + ((_to - _from) * fraction));
        }
    }

    /// <summary>Token check for tight loops that do not report a row.</summary>
    public void Pulse()
    {
        if (_cancellation.CanBeCanceled)
        {
            _cancellation.ThrowIfCancellationRequested();
        }
    }

    public void Complete()
    {
        if (_completed || !IsActive)
        {
            return;
        }

        _completed = true;
        _cancellation.ThrowIfCancellationRequested();
        _progress?.Invoke(_to);
    }

    /// <summary>
    /// The UI only needs about twenty updates per second: the token still gets
    /// checked at every band, but unchanged percentages no longer flood the
    /// dispatcher.
    /// </summary>
    private sealed class ReportThrottle
    {
        private const int MinimumIntervalMilliseconds = 50;
        private readonly long _interval = Math.Max(1, MinimumIntervalMilliseconds) *
            Stopwatch.Frequency / 1000;
        private long _last;

        public bool ShouldReport()
        {
            var now = Stopwatch.GetTimestamp();
            if (now - _last < _interval)
            {
                return false;
            }

            _last = now;
            return true;
        }
    }
}
