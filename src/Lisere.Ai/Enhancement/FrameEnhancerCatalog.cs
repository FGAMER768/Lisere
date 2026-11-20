using Lisere.Ai.Models;

namespace Lisere.Ai.Enhancement;

/// <summary>
/// Builds the engines Lisere can offer. Adding a new model is a catalog entry plus,
/// when the inference contract differs, one new enhancer implementation.
/// </summary>
public static class FrameEnhancerCatalog
{
    public static IReadOnlyList<IFrameEnhancer> Create(ModelCatalog? catalog)
    {
        var engines = new List<IFrameEnhancer>
        {
            new FrameLookEnhancer(new ClassicFrameEnhancer())
        };

        if (catalog is not null)
        {
            // A super-resolution model grows the frame, a restoration model
            // (denoise, deJPEG) returns it at its own size; both are frame
            // engines and share the same two implementations. Same-size models
            // come first, then the upscalers by growing factor.
            var frames = catalog.Models
                .Where(model => IsFrameEngine(model.Manifest.Family))
                .OrderBy(model => model.Manifest.Scale > 1 ? 1 : 0)
                .ThenBy(model => model.Manifest.Scale);

            foreach (var model in frames)
            {
                // A colour model rebuilds the three planes at once; a luma model
                // only sharpens the luminance and resamples the chroma.
                IFrameEnhancer inner = string.Equals(
                    model.Manifest.Plane,
                    "rgb",
                    StringComparison.OrdinalIgnoreCase)
                    ? new OnnxRgbSuperResolutionEnhancer(model)
                    : new OnnxSuperResolutionEnhancer(model);

                engines.Add(new FrameLookEnhancer(inner));
            }
        }

        return engines;
    }

    private static bool IsFrameEngine(string family) =>
        family.Equals("super-resolution", StringComparison.OrdinalIgnoreCase) ||
        family.Equals("restoration", StringComparison.OrdinalIgnoreCase);
}
