using Lisere.Ai.Enhancement;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Core.Imaging;

namespace Lisere.Ai.Tests;

[Collection(GpuDevices.Name)]
public class GpuClassicFrameEnhancerTests
{
    [Fact]
    public void TheWholeClassicPipelineMatchesTheCpuReference()
    {
        var source = CreatePattern(64, 48);
        using var reference = new FrameLookEnhancer(new ClassicFrameEnhancer());
        using var gpu = GpuClassicFrameEnhancer.Create();
        var request = new FrameEnhancementRequest(
            new EnhancementParameters(60, 40, 10, 85, 10, 60),
            RecoverOriginalDetail: 20,
            OutputScale: 2,
            Grading: ColorPreset.UltronCc,
            DetailOnLumaOnly: true);

        var expected = reference.Enhance(source, request, CancellationToken.None);
        var actual = gpu.Enhance(source, request, CancellationToken.None);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void TheGrainRunsOnTheGpuWithTheCpuMask()
    {
        var source = CreatePattern(96, 72);
        using var reference = new FrameLookEnhancer(new ClassicFrameEnhancer());
        using var gpu = GpuClassicFrameEnhancer.Create();
        var request = new FrameEnhancementRequest(
            new EnhancementParameters(60, 40, 10, 85, 10, 60),
            AddNoise: 0.02,
            OutputScale: 2,
            Grading: ColorPreset.UltronCc);

        if (gpu.ActiveProvider is null)
        {
            return;
        }

        var expected = reference.Enhance(source, request, CancellationToken.None);
        var actual = gpu.Enhance(source, request, CancellationToken.None);

        // Le grain doit avoir tourné sur le GPU, pas sur le repli CPU.
        Assert.Equal(1, ((IFrameStageTimingsProvider)gpu).TakeStageTimings().Frames);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    /// <summary>
    /// Une frame 4K dépasse 65 535 groupes de 64 threads : ce test garde le
    /// dispatch au-dessus de cette limite et vérifie que le device survit.
    /// </summary>
    [Fact]
    public void TheGpuHandlesAFourKFrameWithoutDisablingItself()
    {
        var source = CreatePattern(3840, 2160);
        using var gpu = GpuClassicFrameEnhancer.Create();

        if (gpu.ActiveProvider is null)
        {
            return;
        }

        var request = new FrameEnhancementRequest(
            new EnhancementParameters(60, 40, 10, 85, 10, 60));

        var actual = gpu.Enhance(source, request, CancellationToken.None);
        var timings = ((IFrameStageTimingsProvider)gpu).TakeStageTimings();

        Assert.Equal(3840, actual.Width);
        Assert.Equal(2160, actual.Height);
        Assert.Null(gpu.ProviderFallbackReason);
        Assert.Equal(1, timings.Frames);
    }

    [Fact]
    public void DisablingTheGpuKeepsTheCpuPipelineAvailable()
    {
        var source = CreatePattern(32, 24);
        using var reference = new FrameLookEnhancer(new ClassicFrameEnhancer());
        using var fallback = GpuClassicFrameEnhancer.Create(enableGpu: false);
        var request = new FrameEnhancementRequest(
            new EnhancementParameters(30, 20, 5, 40, 5, 30),
            Grading: ColorPreset.Matrix);

        Assert.Null(fallback.ActiveProvider);
        Assert.NotNull(fallback.ProviderFallbackReason);

        var expected = reference.Enhance(source, request, CancellationToken.None);
        var actual = fallback.Enhance(source, request, CancellationToken.None);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static RgbImage CreatePattern(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        var random = new Random(1234);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                var gradient = x * 255 / Math.Max(1, width - 1);
                var wave = (int)(24 * Math.Sin((x + y) * 0.09));
                pixels[offset] = (byte)Math.Clamp(gradient + wave, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(
                    255 - gradient + (wave / 2) + random.Next(-8, 9),
                    0,
                    255);
                pixels[offset + 2] = (byte)Math.Clamp((x * 3 + y * 5) % 256, 0, 255);
            }
        }

        return new RgbImage(width, height, pixels);
    }
}
