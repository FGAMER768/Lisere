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

    public static RgbImage Apply(RgbImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = new RgbImage(source.Width, source.Height);
        for (var i = 0; i < source.Pixels.Length; i++)
        {
            output.Pixels[i] = Curve[source.Pixels[i]];
        }

        return output;
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
