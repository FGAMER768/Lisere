namespace Lisere.Core.Grading;

/// <summary>
/// Valeurs relevées dans <c>Workspace/ChromaEnhance/Infinity_CC.ffx</c>, dans
/// l'ordre de la pile : S_Sharpen, BCC Unsharp Mask, S_Glow, Exposition.
/// </summary>
public static class InfinityTuning
{
    /// <summary>S_Sharpen : amplitude 0,6, seuil de contour 0,15.</summary>
    public static class SapphireSharpen
    {
        public const double Amplitude = 0.6;

        /// <summary>Edge Threshold = 0,149994, sur une échelle 0–1.</summary>
        public const double EdgeThreshold = 0.149993896484375;

        /// <summary>Small Detail Size = 3,8 px : rayon de la bande la plus fine.</summary>
        public const double SmallDetailSize = 3.8;

        /// <summary>
        /// Scale Tiny / Small / Medium / Large Details = 2 / 1,5 / 1 / 2. Les
        /// bandes sont espacées en doublant le rayon à partir de la plus fine,
        /// comme dans <see cref="UltronCcTuning.SapphireSharpen"/>.
        /// </summary>
        public static IReadOnlyList<double> BandWeights { get; } = [2.0, 1.5, 1.0, 2.0];

        /// <summary>Sharpen Luma = 1.</summary>
        public const double LumaAmount = 1.0;

        /// <summary>Sharpen Chroma = 1.</summary>
        public const double ChromaAmount = 1.0;
    }

    /// <summary>
    /// BCC Unsharp Mask : rayon 9,8, 16 %, seuil 0. Le fichier contient un
    /// second « Threshold » (50) dans la section PixelChooser ; celui de l'effet
    /// est le premier, placé juste après Amount.
    /// </summary>
    public static class BccUnsharpMask
    {
        public const double Radius = 9.8;
        public const double AmountPercent = 16.0;
        public const double Threshold = 0.0;
    }

    /// <summary>
    /// S_Glow : luminosité du halo 0,2, seuil 0,6, largeur 96 px, largeurs par
    /// canal 1 / 1,2 / 1,4. « Combine », « Opacity » et « Edge Mode » ne sont
    /// pas reproduits.
    /// </summary>
    public static class SapphireGlow
    {
        public const double Brightness = 0.2;
        public const double Threshold = 0.6;
        public const double Width = 96.0;
        public static IReadOnlyList<double> ChannelWidths { get; } = [1.0, 1.1999969482421875, 1.399993896484375];
    }

    /// <summary>Exposition : −0,36 stop, sans décalage ni correction gamma.</summary>
    public const double Exposure = -0.36000000000000015;
}
