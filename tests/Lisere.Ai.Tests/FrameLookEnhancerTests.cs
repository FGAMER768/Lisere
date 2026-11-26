using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

public class FrameLookEnhancerTests
{
    [Fact]
    public void TheWrapperKeepsTheEngineIdentity()
    {
        using var engine = new ClassicFrameEnhancer();
        using var enhancer = new FrameLookEnhancer(engine);

        Assert.Equal(engine.Id, enhancer.Id);
        Assert.Equal(engine.DisplayName, enhancer.DisplayName);
        Assert.Equal(engine.Family, enhancer.Family);
        Assert.Equal(engine.NativeScale, enhancer.NativeScale);
    }

    [Fact]
    public void TheSelectedGradingIsAppliedAfterTheEngine()
    {
        var source = CreateGradient(16, 16);
        using var engine = new ClassicFrameEnhancer();
        using var enhancer = new FrameLookEnhancer(engine);
        var request = new FrameEnhancementRequest(
            EnhancementParameters.Zero,
            Grading: ColorPreset.Matrix);

        // The video pipeline must match the image pipeline's order:
        // engine → grading.
        var expected = ColorGrading.Apply(
            engine.Enhance(
                source,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                CancellationToken.None),
            ColorPreset.Matrix);
        Assert.Equal(
            expected.Pixels,
            enhancer.Enhance(source, request, CancellationToken.None).Pixels);
    }

    [Fact]
    public void CustomUltronSettingsReachTheLook()
    {
        // Taille d'une vraie frame réduite : sur une image de 16 px, les
        // rayons du look dépassent l'image et le seuil de contour tombe sur
        // des valeurs limites, sans rapport avec le rendu vidéo.
        var source = CreateGradient(96, 54);
        using var engine = new ClassicFrameEnhancer();
        using var enhancer = new FrameLookEnhancer(engine);
        var request = new FrameEnhancementRequest(
            EnhancementParameters.Zero,
            Grading: ColorPreset.UltronCc);
        var settings = new UltronCcSettings(70, -20, 30, 10);

        var standard = enhancer.Enhance(source, request, CancellationToken.None);
        var custom = enhancer.Enhance(
            source,
            request with { GradingSettings = settings },
            CancellationToken.None);

        var expected = ColorGrading.Apply(
            engine.Enhance(
                source,
                new FrameEnhancementRequest(EnhancementParameters.Zero),
                CancellationToken.None),
            ColorPreset.UltronCc,
            settings);

        Assert.NotEqual(standard.Pixels, custom.Pixels);

        // L'étalonnage tourne sur le GPU : la saturation et la vibrance y sont
        // en simple précision. Un octet peut donc basculer d'un niveau, et
        // franchir du même coup le seuil de contour de S_Sharpen, qui ajoute
        // alors tout le détail de la bande : trois niveaux au pire, sur un
        // pixel isolé (voir GpuLookParityTests pour la mesure sur image réelle).
        AssertClose(expected.Pixels, custom.Pixels, maximum: 3);
    }

    private static void AssertClose(byte[] expected, byte[] actual, int maximum)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            var delta = Math.Abs(expected[index] - actual[index]);
            Assert.True(
                delta <= maximum,
                $"octet {index} : {expected[index]} attendu, {actual[index]} obtenu, écart {delta} > {maximum}.");
        }
    }

    private static RgbImage CreateGradient(int width, int height)
    {
        var image = new RgbImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)((x * 255) / (width - 1));
                image[x, y, 0] = value;
                image[x, y, 1] = (byte)(255 - value);
                image[x, y, 2] = (byte)((value + y * 16) % 256);
            }
        }

        return image;
    }
}
