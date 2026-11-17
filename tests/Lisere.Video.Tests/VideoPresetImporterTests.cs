using Lisere.Video.Presets;

namespace Lisere.Video.Tests;

public class VideoPresetImporterTests
{
    [Fact]
    public void ReadsTheSixSlidersAndRecovery()
    {
        var preset = VideoPresetImporter.Parse("""
            {
              "name": "Chroma",
              "settings": {
                "enhance": {
                  "model": "prob-3",
                  "auto": 2,
                  "compress": 60,
                  "denoise": 40,
                  "sharpen": 10,
                  "detail": 85,
                  "dehalo": 10,
                  "deblur": 95,
                  "recoverOriginalDetailValue": 20,
                  "addNoise": 0.01
                }
              }
            }
            """);

        Assert.Equal("Chroma", preset.Name);
        Assert.Equal("prob-3", preset.ModelId);
        Assert.Equal(60, preset.Parameters.FixCompression);
        Assert.Equal(40, preset.Parameters.ReduceNoise);
        Assert.Equal(10, preset.Parameters.Sharpen);
        Assert.Equal(85, preset.Parameters.RecoverDetails);
        Assert.Equal(10, preset.Parameters.Dehalo);
        Assert.Equal(95, preset.Parameters.AntialiasDeblur);
        Assert.Equal(20, preset.RecoverOriginalDetail);
        Assert.Equal(0.01, preset.AddNoise);
        Assert.False(preset.Auto);
        Assert.Null(preset.TargetHeight);
        Assert.Null(preset.TargetFrameRate);
    }

    [Fact]
    public void AnEmptyAutoPresetBecomesALisereAutoPreset()
    {
        var preset = VideoPresetImporter.Parse("""
            {
              "name": "General Enhancement",
              "settings": {
                "enhance": { "model": "prob-4", "auto": 1 }
              }
            }
            """);

        Assert.True(preset.Auto);
        Assert.False(preset.HasEnhancementParameters);
        Assert.Equal("prob-4", preset.ModelId);
    }

    [Fact]
    public void ReadsFhdSizeAndCadenceFromARealPresetShape()
    {
        var preset = VideoPresetImporter.Parse("""
            {
              "name": "Upscale to FHD and convert to 60 fps",
              "settings": {
                "enhance": { "model": "prob-4", "auto": 0, "compress": 10 },
                "output": { "outSizeMethod": 6, "outFPS": 60 },
                "stabilize": { "active": true },
                "deinterlace": { "active": false }
              }
            }
            """);

        Assert.Equal(1080, preset.TargetHeight);
        Assert.Equal(60, preset.TargetFrameRate);
        Assert.True(preset.Stabilize);
        Assert.False(preset.Deinterlace);
    }

    [Fact]
    public void ReadsAnExplicitHeightAnd4kMethod()
    {
        var explicitHeight = VideoPresetImporter.Parse("""
            {
              "settings": {
                "output": { "outSizeMethod": 9, "height": "720", "outputFps": "1x" }
              }
            }
            """,
            "MiniDV");

        Assert.Equal("MiniDV", explicitHeight.Name);
        Assert.Equal(720, explicitHeight.TargetHeight);
        Assert.Null(explicitHeight.TargetFrameRate);

        var ultraHd = VideoPresetImporter.Parse("""
            { "settings": { "output": { "outSizeMethod": 7 } } }
            """);

        Assert.Equal(2160, ultraHd.TargetHeight);
    }

    [Fact]
    public void GrainIsApproximatedWhenEnhanceHasNoNoise()
    {
        var preset = VideoPresetImporter.Parse("""
            {
              "settings": {
                "enhance": { "addNoise": 0 },
                "grain": { "active": true, "grainStrength": 50 }
              }
            }
            """);

        Assert.Equal(0.005, preset.AddNoise, 3);
    }

    [Fact]
    public void RejectsInvalidJson()
    {
        Assert.ThrowsAny<Exception>(() => VideoPresetImporter.Parse("not json"));
        Assert.Throws<FormatException>(() => VideoPresetImporter.Parse("[]"));
    }

    [Fact]
    public void ParsesAnExternalPresetCorpusWhenConfigured()
    {
        var presetsDirectory = FindPresetDirectory();
        if (presetsDirectory is null)
        {
            return;
        }

        var presets = Directory
            .EnumerateFiles(presetsDirectory, "*.json")
            .Select(VideoPresetImporter.ParseFile)
            .ToArray();

        Assert.True(presets.Length >= 20, $"Only {presets.Length} presets were parsed.");
        Assert.All(presets, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.InRange(preset.Parameters.FixCompression, 0, 100);
            Assert.InRange(preset.Parameters.AntialiasDeblur, -100, 100);
            Assert.InRange(preset.RecoverOriginalDetail, 0, 100);
            Assert.InRange(preset.AddNoise, 0, 0.1);
        });
    }

    private static string? FindPresetDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("LISERE_PRESET_SAMPLE_DIR");
        return !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            ? configured
            : null;
    }
}
