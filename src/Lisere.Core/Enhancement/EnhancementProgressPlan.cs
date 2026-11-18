using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

/// <summary>
/// Distributes the pipeline percentage over the passes that actually run, then
/// lets each pass report its own row-level fraction. The pipeline reports
/// 0..100 of its own work; the orchestrator maps that onto the share that
/// leaves room for grading and preview, so the dialog never shows 100 early.
/// </summary>
internal sealed class EnhancementProgressPlan
{
    private readonly IReadOnlyList<(EnhancementStage Stage, double Weight)> _stages;
    private readonly IProgress<EnhancementProgress>? _progress;
    private readonly CancellationToken _cancellation;
    private readonly double _totalWeight;
    private readonly int _bandRows;
    private int _index;
    private double _completedWeight;

    public EnhancementProgressPlan(
        IReadOnlyList<(EnhancementStage Stage, double Weight)> stages,
        IProgress<EnhancementProgress>? progress,
        CancellationToken cancellationToken,
        int bandRows = ImagingWork.DefaultBandRows)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
        {
            throw new ArgumentException("At least one stage is required.", nameof(stages));
        }

        if (stages.Any(stage => stage.Weight <= 0.0))
        {
            throw new ArgumentException("Stage weights must be positive.", nameof(stages));
        }

        _stages = stages;
        _progress = progress;
        _cancellation = cancellationToken;
        _bandRows = bandRows;
        _totalWeight = stages.Sum(stage => stage.Weight);
    }

    public ImagingWork? Begin(EnhancementStage stage)
    {
        _cancellation.ThrowIfCancellationRequested();

        if (_index >= _stages.Count || _stages[_index].Stage != stage)
        {
            throw new InvalidOperationException(
                $"Expected stage {(_index < _stages.Count ? _stages[_index].Stage : EnhancementStage.Finish)} " +
                $"but got {stage}.");
        }

        if (_progress is null && !_cancellation.CanBeCanceled)
        {
            return null;
        }

        var weight = _stages[_index].Weight;
        var start = _completedWeight;
        Action<double>? report = _progress is null
            ? null
            : fraction =>
            {
                var percent = 100.0 *
                    (start + (weight * Math.Clamp(fraction, 0.0, 1.0))) /
                    _totalWeight;
                _progress.Report(new EnhancementProgress(
                    stage,
                    Math.Min(100.0, percent)));
            };

        report?.Invoke(0.0);
        return new ImagingWork(_cancellation, report, _bandRows);
    }

    public void Complete()
    {
        if (_index >= _stages.Count)
        {
            throw new InvalidOperationException("No stage is running.");
        }

        _completedWeight += _stages[_index].Weight;
        _index++;
    }
}
