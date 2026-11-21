using Lisere.Ai.Models;

namespace Lisere.Ai.Tests;

public class ModelCatalogTests
{
    [Fact]
    public void TheShippedCatalogIsDiscoveredAndVerified()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);

        var catalog = ModelCatalog.Load(directory);
        var model = Assert.Single(
            catalog.Models,
            candidate => candidate.Manifest.Id == "onnx-srcnn-3x");

        Assert.True(model.IsVerified, model.Issue);
        Assert.Equal(3, model.Manifest.Scale);
        Assert.Equal("input", model.Manifest.InputName);
        Assert.Equal("output", model.Manifest.OutputName);

        var rife = Assert.Single(
            catalog.Models,
            candidate => candidate.Manifest.Family == "frame-interpolation");
        Assert.True(rife.IsVerified, rife.Issue);

        // The RGB engine only runs when its pinned export matches byte for byte.
        var colour = Assert.Single(
            catalog.Models,
            candidate => candidate.Manifest.Id == "realesr-general-x4v3");
        Assert.True(colour.IsVerified, colour.Issue);
        Assert.Equal("rgb", colour.Manifest.Plane);
        Assert.Equal(4, colour.Manifest.Scale);

        var restoration = Assert.Single(
            catalog.Models,
            candidate => candidate.Manifest.Id == "realplksr-denoise-1x");
        Assert.True(restoration.IsVerified, restoration.Issue);
        Assert.Equal(1, restoration.Manifest.Scale);
    }

    [Fact]
    public void ALocalModelWithoutFingerprintRunsAndIsFlagged()
    {
        var source = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(source);

        var temporary = Path.Combine(Path.GetTempPath(), $"lisere-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(temporary, "onnx"));
        Directory.CreateDirectory(Path.Combine(temporary, "custom"));

        try
        {
            File.Copy(
                Path.Combine(source, "catalog.json"),
                Path.Combine(temporary, "catalog.json"));
            File.WriteAllBytes(Path.Combine(temporary, "onnx", "mine.onnx"), [1, 2, 3]);
            WriteManifest(temporary, "mine.json", "mon-modele", sha256: string.Empty);
            WriteManifest(temporary, "wrong.json", "empreinte-fausse", sha256: "deadbeef");

            var catalog = ModelCatalog.Load(temporary);

            var local = Assert.Single(
                catalog.Models,
                candidate => candidate.Manifest.Id == "mon-modele");
            Assert.False(local.IsVerified);
            Assert.True(local.IsRunnable);
            Assert.Contains("empreinte", local.Issue, StringComparison.Ordinal);

            // Une empreinte fournie mais fausse reste un refus, pas un badge.
            var wrong = Assert.Single(
                catalog.Models,
                candidate => candidate.Manifest.Id == "empreinte-fausse");
            Assert.False(wrong.IsRunnable);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WriteManifest(
        string directory,
        string fileName,
        string id,
        string sha256) =>
        File.WriteAllText(
            Path.Combine(directory, "custom", fileName),
            $$"""
            {
              "id": "{{id}}",
              "displayName": "{{id}}",
              "family": "restoration",
              "file": "onnx/mine.onnx",
              "sha256": "{{sha256}}",
              "scale": 1,
              "inputName": "input",
              "outputName": "output",
              "tileSize": 256,
              "plane": "rgb",
              "license": "local",
              "source": "utilisateur",
              "description": "Modèle déposé à la main."
            }
            """);

    [Fact]
    public void ATamperedModelIsReportedUnverified()
    {
        var source = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(source);

        var temporary = Path.Combine(Path.GetTempPath(), $"lisere-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(temporary, "onnx"));

        try
        {
            File.Copy(
                Path.Combine(source, "catalog.json"),
                Path.Combine(temporary, "catalog.json"));
            File.WriteAllBytes(
                Path.Combine(temporary, "onnx", "super-resolution-10.onnx"),
                [1, 2, 3, 4]);

            var catalog = ModelCatalog.Load(temporary);
            var model = Assert.Single(
                catalog.Models,
                candidate => candidate.Manifest.Id == "onnx-srcnn-3x");

            Assert.False(model.IsVerified);
            Assert.Contains("SHA-256", model.Issue, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
