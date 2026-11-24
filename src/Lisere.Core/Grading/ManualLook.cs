using Lisere.Core.Imaging;

namespace Lisere.Core.Grading;

/// <summary>
/// Etalonnage manuel : saturation, luminosité, contraste et vibrance, sans les
/// accentuations propres au look Ultron. Les quatre réglages sont ceux exposés
/// par les curseurs des pages Image et Vidéo.
/// </summary>
public static class ManualLook
{
    // Rec. 709, la même pondération que les autres looks.
    private const double LumaRed = 0.2126;
    private const double LumaGreen = 0.7152;
    private const double LumaBlue = 0.0722;

    public static RgbImage Apply(
        RgbImage source,
        UltronCcSettings settings,
        ImagingWork? work = null,
        RgbImagePool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        var output = Math.Abs(settings.SaturationPercent - 100.0) < 0.0001
            ? source
            : GradingFilters.WeightedSaturation(
                source,
                settings.SaturationPercent / 100.0,
                LumaRed,
                LumaGreen,
                LumaBlue,
                work?.Slice(0, 0.45),
                pool,
                cancellationToken);

        if (settings.Brightness != 0.0 || settings.Contrast != 0.0)
        {
            var next = GradingFilters.BrightnessContrast(
                output,
                settings.Brightness,
                settings.Contrast,
                pivot: 0.5,
                work?.Slice(0.45, 0.80),
                pool,
                cancellationToken);
            output = GradingChain.Advance(pool, source, output, next);
        }

        if (settings.Vibrance != 0.0)
        {
            var next = GradingFilters.Vibrance(
                output,
                settings.Vibrance,
                LumaRed,
                LumaGreen,
                LumaBlue,
                skinProtection: 1.0,
                skinHueStart: 0.0,
                skinHueEnd: 0.0,
                work?.Slice(0.80, 1.0),
                pool,
                cancellationToken);
            output = GradingChain.Advance(pool, source, output, next);
        }

        return ReferenceEquals(output, source) ? GradingChain.Copy(pool, source) : output;
    }
}
