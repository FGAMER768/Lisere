using Lisere.Core.Imaging;

namespace Lisere.Ai.Interpolation;

/// <summary>
/// Produces an intermediate frame between two adjacent frames. Implementations
/// are independent of the video I/O layer so they can be tested on images.
/// </summary>
public interface IFrameInterpolator : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    string ExecutionProvider { get; }

    RgbImage Interpolate(
        RgbImage first,
        RgbImage second,
        double timestep,
        CancellationToken cancellationToken);
}
