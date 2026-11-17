namespace Lisere.Core.Grading;

/// <summary>
/// Valeurs relevées dans <c>Workspace/ChromaEnhance/Nebulus_CC.ffx</c>, dans
/// l'ordre de la pile : S_Sharpen, BCC Unsharp Mask, S_Glow.
/// </summary>
public static class NebulusTuning
{
    /// <summary>S_Sharpen : amplitude 0,6, seuil de contour 0,15.</summary>
    public static class SapphireSharpen
    {
        public const double Amplitude = 0.6;

        /// <summary>Edge Threshold = 0,149994, sur une échelle 0–1.</summary>
        public const double EdgeThreshold = 0.149993896484375;

        /// <summary>Small Detail Size = 4 px : rayon de la bande la plus fine.</summary>
        public const double SmallDetailSize = 4.0;

        /// <summary>
        /// Scale Tiny / Small / Medium / Large Details = 3 / 1,5 / 1 / 2, avec
        /// un espacement doublé à partir de la bande la plus fine.
        /// </summary>
        public static IReadOnlyList<double> BandWeights { get; } = [3.0, 1.5, 1.0, 2.0];

        /// <summary>Sharpen Luma = 1.</summary>
        public const double LumaAmount = 1.0;

        /// <summary>Sharpen Chroma = 1.</summary>
        public const double ChromaAmount = 1.0;
    }

    /// <summary>
    /// BCC Unsharp Mask : rayon 20, 10 %, seuil 0. Le « Threshold » de la section
    /// PixelChooser (50) n'est pas celui de l'effet.
    /// </summary>
    public static class BccUnsharpMask
    {
        public const double Radius = 20.0;
        public const double AmountPercent = 10.0;
        public const double Threshold = 0.0;
    }

    /// <summary>
    /// S_Glow : luminosité du halo 0,65, seuil 0,5, largeur 96 px, largeurs par
    /// canal 1 / 1,2 / 1,4. « Combine », « Opacity » et « Edge Mode » ne sont
    /// pas reproduits.
    /// </summary>
    public static class SapphireGlow
    {
        public const double Brightness = 0.6499999999999997;
        public const double Threshold = 0.5;
        public const double Width = 96.0;
        public static IReadOnlyList<double> ChannelWidths { get; } = [1.0, 1.1999969482421875, 1.399993896484375];
    }
}
