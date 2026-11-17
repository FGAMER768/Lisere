using System.IO;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class ImageIngestionTests
{
    [Fact]
    public async Task LoadAppliesExifOrientationBeforeTheImageIsDisplayed()
    {
        var image = await Load("orientation6.jpg");

        Assert.Equal(4, image.Width);
        Assert.Equal(8, image.Height);
    }

    [Fact]
    public async Task LoadKeepsExifOrientationQuadrantsInTheRightPlaces()
    {
        var image = await Load("orientation6-quadrants.jpg");

        Assert.Equal(20, image.Width);
        Assert.Equal(40, image.Height);
        Assert.True(image[5, 10, 2] > 180, "Le quart bleu doit occuper le coin supérieur gauche.");
        Assert.True(image[15, 10, 0] > 180, "Le quart rouge doit occuper le coin supérieur droit.");
        Assert.True(image[5, 30, 0] > 180 && image[5, 30, 1] > 180,
            "Le quart jaune doit occuper le coin inférieur gauche.");
        Assert.True(image[15, 30, 1] > 180, "Le quart vert doit occuper le coin inférieur droit.");
    }

    [Theory]
    [InlineData("orientation6.png")]
    [InlineData("orientation6.tiff")]
    [InlineData("orientation6.webp")]
    public async Task LoadAppliesOrientationMetadataFromOtherContainers(string fileName)
    {
        var image = await Load(fileName);

        Assert.Equal(20, image.Width);
        Assert.Equal(40, image.Height);
    }

    [Fact]
    public async Task LoadCompositesTransparentPixelsOverWhite()
    {
        var image = await Load("alpha.png");

        Assert.Equal(255, image[0, 0, 0]);
        Assert.Equal(255, image[0, 0, 1]);
        Assert.Equal(255, image[0, 0, 2]);
        Assert.InRange(image[1, 0, 0], 110, 145);
        Assert.InRange(image[1, 0, 1], 240, 255);
        Assert.InRange(image[1, 0, 2], 110, 145);
    }

    [Theory]
    [InlineData("cmyk.jpg", 8, 4)]
    [InlineData("gray16.png", 8, 4)]
    [InlineData("indexed.gif", 8, 4)]
    [InlineData("image.webp", 8, 4)]
    [InlineData("progressive.jpg", 8, 4)]
    [InlineData("odd.bmp", 3, 2)]
    public async Task LoadReadsIrregularImageFormats(
        string fileName,
        int expectedWidth,
        int expectedHeight)
    {
        var image = await Load(fileName);

        Assert.Equal(expectedWidth, image.Width);
        Assert.Equal(expectedHeight, image.Height);
    }

    [Fact]
    public void FilePickerFilterCoversCommonAndWicFormats()
    {
        var filter = new FileDialogService(new LocalizationService()).ImageFilter;

        Assert.Contains("*.png", filter);
        Assert.Contains("*.jpg", filter);
        Assert.Contains("*.gif", filter);
        Assert.Contains("*.ico", filter);
        Assert.Contains("*.tif", filter);
        Assert.Contains("*.webp", filter);
        Assert.Contains("*.jxr", filter);
        Assert.Contains("Tous les fichiers", filter);
    }

    [Fact]
    public void FilePickerFilterFollowsTheSelectedLanguage()
    {
        var localization = new LocalizationService();
        var service = new FileDialogService(localization);

        localization.SetLanguage(AppLanguage.English);

        Assert.Contains("All files", service.ImageFilter);
        Assert.DoesNotContain("Tous les fichiers", service.ImageFilter);
    }

    private static Task<Core.Imaging.RgbImage> Load(string fileName) =>
        new ImageCodec().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", fileName),
            CancellationToken.None);
}
