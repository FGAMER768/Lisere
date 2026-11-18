using System.IO;
using Lisere.Core.Enhancement;
using Lisere.Core.Grading;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class LiserePresetSerializerTests
{
    private const string CompleteJson = """
        {
          "format": "lisere-preset",
          "version": 1,
          "name": "Portrait Topaz",
          "enhancement": {
            "mode": "chromaEnhanceMain",
            "parameters": {
              "fixCompression": 60,
              "reduceNoise": 40,
              "sharpen": 10,
              "recoverDetails": 85,
              "dehalo": 10,
              "antialiasDeblur": 95
            },
            "recoverOriginalDetail": 20,
            "addNoise": 0
          },
          "color": {
            "preset": "ultronCc",
            "settings": {
              "saturationPercent": 129.997,
              "brightness": 5,
              "contrast": 7,
              "vibrance": 50
            }
          },
          "futureField": "ignored"
        }
        """;

    [Fact]
    public void RoundTripKeepsBothSections()
    {
        var preset = new LiserePreset(
            "Mon rendu",
            EnhancementMode.Manual,
            new EnhancementParameters(61, 39, 12, 86, 11, 94),
            17,
            0.015,
            ColorPreset.UltronCc,
            new UltronCcSettings(150, 10, 12, 60));

        var json = LiserePresetSerializer.Serialize(preset);
        var parsed = LiserePresetSerializer.Parse(json);

        Assert.Equal(preset, parsed);
        Assert.Contains("\"format\": \"lisere-preset\"", json);
        Assert.Contains("\"fixCompression\": 61", json);
        Assert.Contains("\"saturationPercent\": 150", json);
        Assert.Contains("\"name\": \"Mon rendu\"", json);
    }

    [Fact]
    public void ParseAcceptsTheDocumentedShapeAndIgnoresUnknownFields()
    {
        var preset = LiserePresetSerializer.Parse(CompleteJson);

        Assert.Equal("Portrait Topaz", preset.Name);
        Assert.Equal(EnhancementMode.ChromaEnhanceMain, preset.Mode);
        Assert.Equal(EnhancementPresets.ChromaEnhanceMain.Parameters, preset.Parameters);
        Assert.Equal(20, preset.RecoverOriginalDetail);
        Assert.Equal(ColorPreset.UltronCc, preset.ColorPreset);
        Assert.Equal(129.997, preset.ColorSettings.SaturationPercent, 3);
        Assert.Equal(5, preset.ColorSettings.Brightness);
    }

    [Fact]
    public void ParseClampsValuesToTheSliderRanges()
    {
        var json = CompleteJson
            .Replace("\"fixCompression\": 60", "\"fixCompression\": 999")
            .Replace("\"recoverDetails\": 85", "\"recoverDetails\": -5")
            .Replace("\"antialiasDeblur\": 95", "\"antialiasDeblur\": -999")
            .Replace("\"addNoise\": 0", "\"addNoise\": 5")
            .Replace("\"saturationPercent\": 129.997", "\"saturationPercent\": 999")
            .Replace("\"brightness\": 5", "\"brightness\": -999")
            .Replace("\"contrast\": 7", "\"contrast\": 999")
            .Replace("\"vibrance\": 50", "\"vibrance\": 999");

        var preset = LiserePresetSerializer.Parse(json);

        Assert.Equal(100, preset.Parameters.FixCompression);
        Assert.Equal(0, preset.Parameters.RecoverDetails);
        Assert.Equal(-100, preset.Parameters.AntialiasDeblur);
        Assert.Equal(EnhancementTuning.Noise.Maximum, preset.AddNoise);
        Assert.Equal(300, preset.ColorSettings.SaturationPercent);
        Assert.Equal(-150, preset.ColorSettings.Brightness);
        Assert.Equal(100, preset.ColorSettings.Contrast);
        Assert.Equal(100, preset.ColorSettings.Vibrance);
    }

    [Fact]
    public void ParseFallsBackToTheFileNameWhenThePresetHasNoName()
    {
        var json = CompleteJson.Replace("\"name\": \"Portrait Topaz\"", "\"name\": \"  \"");

        Assert.Equal("portrait", LiserePresetSerializer.Parse(json, "portrait").Name);
        Assert.Equal("Preset Liseré", LiserePresetSerializer.Parse(json).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"other\",\"version\":1}")]
    [InlineData("{\"format\":\"lisere-preset\",\"version\":2}")]
    [InlineData("{\"format\":\"lisere-preset\",\"version\":1,\"color\":{\"preset\":\"none\"}}")]
    [InlineData("{\"format\":\"lisere-preset\",\"version\":1,\"enhancement\":{\"parameters\":{}}}")]
    [InlineData("{\"format\":\"lisere-preset\",\"version\":1,\"enhancement\":{\"parameters\":{\"fixCompression\":1,\"reduceNoise\":1,\"sharpen\":1,\"recoverDetails\":1,\"dehalo\":1,\"antialiasDeblur\":1}},\"color\":{}}")]
    public void ParseRejectsMalformedOrIncompleteDocuments(string json)
    {
        Assert.Throws<FormatException>(() => LiserePresetSerializer.Parse(json));
    }

    [Fact]
    public void ParseRejectsAMissingFormatOrVersion()
    {
        var noFormat = CompleteJson.Replace("\"format\": \"lisere-preset\",", string.Empty);
        var noVersion = CompleteJson.Replace("\"version\": 1,", string.Empty);

        Assert.Throws<FormatException>(() => LiserePresetSerializer.Parse(noFormat));
        Assert.Throws<FormatException>(() => LiserePresetSerializer.Parse(noVersion));
    }

    [Fact]
    public void ParseRejectsUnknownModesAndColorPresets()
    {
        var unknownMode = CompleteJson.Replace("\"chromaEnhanceMain\"", "\"turbo\"");
        var unknownColor = CompleteJson.Replace("\"ultronCc\"", "\"sepia\"");

        Assert.Throws<FormatException>(() => LiserePresetSerializer.Parse(unknownMode));
        Assert.Throws<FormatException>(() => LiserePresetSerializer.Parse(unknownColor));
    }

    [Fact]
    public void SaveAndLoadRoundTripThroughAFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-preset-{Guid.NewGuid():N}.json");
        var preset = new LiserePreset(
            "Fichier",
            EnhancementMode.ChromaEnhanceMain,
            EnhancementPresets.ChromaEnhanceMain.Parameters,
            20,
            0,
            ColorPreset.Matrix,
            UltronCcSettings.FromPreset);

        try
        {
            LiserePresetSerializer.Save(path, preset);
            var loaded = LiserePresetSerializer.Load(path);

            Assert.Equal(preset, loaded);
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
