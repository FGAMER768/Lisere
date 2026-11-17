using Lisere.Core.Enhancement;

namespace Lisere.Core.Tests;

public class ParameterTests
{
    [Fact]
    public void ClampUsesTheGuiRanges()
    {
        var parameters = new EnhancementParameters(-10, 120, -5, 200, 300, -400).Clamp();

        Assert.Equal(0, parameters.FixCompression);
        Assert.Equal(100, parameters.ReduceNoise);
        Assert.Equal(0, parameters.Sharpen);
        Assert.Equal(100, parameters.RecoverDetails);
        Assert.Equal(100, parameters.Dehalo);
        Assert.Equal(-100, parameters.AntialiasDeblur);
    }

    [Fact]
    public void ModelScaleDividesByOneHundred()
    {
        var parameters = new EnhancementParameters(60, 40, 10, 85, 10, 95).ToModelScale();

        Assert.Equal(0.6, parameters.Compression, 12);
        Assert.Equal(0.4, parameters.Noise, 12);
        Assert.Equal(0.1, parameters.Blur, 12);
        Assert.Equal(0.85, parameters.Details, 12);
        Assert.Equal(0.1, parameters.Halo, 12);
        Assert.Equal(0.95, parameters.PreBlur, 12);
    }

    [Fact]
    public void ChromaEnhanceMainMatchesThePresetFile()
    {
        var preset = EnhancementPresets.ChromaEnhanceMain;

        Assert.Equal(new EnhancementParameters(60, 40, 10, 85, 10, 95), preset.Parameters);
        Assert.Equal(20, preset.RecoverOriginalDetail);
        Assert.Equal(0, preset.AddNoise);
    }

    [Theory]
    [InlineData(RelativeLevel.Low, 10, -100, 5, -100, -25, -50)]
    [InlineData(RelativeLevel.Medium, 45, -22, 32, -29, 0, 0)]
    [InlineData(RelativeLevel.High, 100, 100, 100, 46, -100, -100)]
    public void RelativeOffsetsMatchTheModelFiles(
        RelativeLevel level,
        double compression,
        double noise,
        double sharpen,
        double details,
        double dehalo,
        double preBlur)
    {
        var offsets = EnhancementPresets.GetRelativeOffsets(level);

        Assert.Equal(new EnhancementParameters(compression, noise, sharpen, details, dehalo, preBlur), offsets);
    }

    [Fact]
    public void ApplyRelativeAddsThenClamps()
    {
        var baseline = new EnhancementParameters(80, 90, 90, 90, 20, 90);
        var result = EnhancementPresets.ApplyRelative(baseline, RelativeLevel.High);

        Assert.Equal(new EnhancementParameters(100, 100, 100, 100, 0, -10), result);
    }

    [Fact]
    public void TheReferencePresetStaysInsideTheGuidance()
    {
        Assert.Empty(ParameterValidator.Validate(EnhancementPresets.ChromaEnhanceMain.Parameters));
    }

    [Fact]
    public void RecoverDetailsIsOnlyWarnedAboveTheGuidanceCeiling()
    {
        Assert.Empty(Codes(new EnhancementParameters(0, 0, 0, 90, 0, 0)));
        Assert.Equal(
            [ParameterWarningCode.RecoverDetailsAboveGuidance],
            Codes(new EnhancementParameters(0, 0, 0, 90.1, 0, 0)));
    }

    [Fact]
    public void DehaloIsWarnedPastItsSweetSpot()
    {
        Assert.Equal(
            [ParameterWarningCode.DehaloPastSweetSpot],
            Codes(new EnhancementParameters(0, 0, 0, 0, 25.1, 0)));
    }

    [Fact]
    public void CombinedRisksAreWarnedWithoutTheDetailsGuidance()
    {
        Assert.Equal(
            [ParameterWarningCode.HighSharpenWithHighDetail],
            Codes(new EnhancementParameters(0, 0, 60.1, 40.1, 0, 0)));
        Assert.Equal(
            [ParameterWarningCode.HeavyCompressionWithHeavyDetail],
            Codes(new EnhancementParameters(70.1, 0, 0, 50.1, 0, 0)));
    }

    private static ParameterWarningCode[] Codes(EnhancementParameters parameters) =>
        [.. ParameterValidator.Validate(parameters).Select(warning => warning.Code)];
}
