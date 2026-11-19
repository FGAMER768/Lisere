using System.IO;
using Lisere.Ai.Faces;
using Lisere.Ai.Models;
using Lisere.Ai.Onnx;
using Lisere.Services;

namespace Lisere.App.Tests;

public class FaceDetectorTests
{
    [Fact]
    public async Task DetectsTheFaceInTheReferencePortrait()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var model = Path.Combine(directory, "onnx", "face_detection_yunet_2023mar.onnx");
        Assert.True(File.Exists(model), $"modèle YuNet absent : {model}");

        var image = await new ImageCodec().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "face.jpg"),
            CancellationToken.None);

        using var detector = new YuNetFaceDetector(model, OnnxExecutionProvider.Cpu);
        var faces = detector.Detect(image);

        var face = Assert.Single(faces);
        Assert.True(face.Score > 0.6, $"score trop faible ({face.Score:0.00})");
        Assert.InRange(face.X, 0.0, image.Width * 0.25);
        Assert.InRange(face.Y, 0.0, image.Height * 0.35);
        Assert.InRange(face.Width, image.Width * 0.4, image.Width * 0.95);
    }
}
