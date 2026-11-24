using Lisere.Core.Imaging;

namespace Lisere.Core.Enhancement;

/// <summary>
/// Courbe de tons relevée entre notre rendu et la référence Topaz
/// (<c>Base.png</c> → <c>UltronCCReel.png</c>, préréglage Chroma puis
/// Ultron CC), par appariement des quantiles de luminance.
/// Elle relève les tons moyens puis aplatit la courbe au-delà de 192 pour
/// conserver la séparation des hautes lumières : le modèle Topaz éclaircit
/// l'image d'environ 16 niveaux au médian, ce que l'accentuation seule ne
/// reproduit pas, mais relever aussi les blancs dégradait les images claires.
/// </summary>
public static class ChromaTone
{
    private static readonly (int Input, int Output)[] ControlPoints =
    [
        (0, 0),
        (32, 36),
        (64, 75),
        (96, 108),
        (128, 142),
        (160, 175),
        (192, 210),
        (224, 233),
        (255, 255),
    ];

    private static readonly byte[] Curve = BuildCurve();

    /// <summary>
    /// Les 256 entrées de la courbe, pour un moteur qui l'applique ailleurs
    /// (GPU) : la table reste la seule source de vérité.
    /// </summary>
    public static ReadOnlySpan<byte> Table => Curve;

    public static RgbImage Apply(RgbImage source) => Apply(source, pool: null);

    /// <summary>
    /// Applique la courbe en parallèle. Avec une réserve, le tampon de sortie
    /// est loué puis rendu par l'appelant au lieu d'être alloué par frame.
    /// </summary>
    public static RgbImage Apply(
        RgbImage source,
        RgbImagePool? pool,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = pool?.Rent(source.Width, source.Height)
            ?? new RgbImage(source.Width, source.Height);
        ApplyInto(source, output, cancellationToken);
        return output;
    }

    /// <summary>Écrit la courbe dans <paramref name="destination"/> sans allocation.</summary>
    public static void ApplyInto(
        RgbImage source,
        RgbImage destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Width != destination.Width ||
            source.Height != destination.Height)
        {
            throw new ArgumentException(
                "The destination must have the same size as the source.",
                nameof(destination));
        }

        var sourcePixels = source.Pixels;
        var targetPixels = destination.Pixels;
        var stride = source.Width * 3;

        RowBands.For(
            source.Height,
            cancellationToken,
            (start, end) =>
            {
                var offset = start * stride;
                var limit = end * stride;
                for (; offset < limit; offset++)
                {
                    targetPixels[offset] = Curve[sourcePixels[offset]];
                }
            });
    }

    private static byte[] BuildCurve()
    {
        var curve = new byte[256];
        var segment = 0;

        for (var value = 0; value < curve.Length; value++)
        {
            while (segment < ControlPoints.Length - 2 && value > ControlPoints[segment + 1].Input)
            {
                segment++;
            }

            var (input0, output0) = ControlPoints[segment];
            var (input1, output1) = ControlPoints[segment + 1];
            var position = (value - input0) / (double)(input1 - input0);
            curve[value] = (byte)Math.Round(output0 + (position * (output1 - output0)));
        }

        return curve;
    }
}
