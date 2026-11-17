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
        var engines = new List<IFrameEnhancer> { new ClassicFrameEnhancer() };

        if (catalog is not null)
        {
            foreach (var model in catalog.Models.Where(model =>
                string.Equals(
                    model.Manifest.Family,
                    "super-resolution",
                    StringComparison.OrdinalIgnoreCase)))
            {
                engines.Add(new OnnxSuperResolutionEnhancer(model));
            }
        }

        return engines;
    }
}
