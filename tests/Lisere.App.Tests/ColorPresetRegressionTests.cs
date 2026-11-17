using System.IO;
using System.Security.Cryptography;
using Lisere.Core.Grading;
using Lisere.Services;

namespace Lisere.App.Tests;

/// <summary>
/// Non-régression des cinq presets de colorimétrie sur une photo de test
/// versionnée (320 × 213, Lorem Picsum / licence Unsplash). Chaque empreinte
/// couvre les pixels entiers : toute modification de formule, d'ordre d'effet
/// ou de valeur du preset la fait changer.
/// </summary>
public class ColorPresetRegressionTests
{
    [Theory]
    [InlineData(ColorPreset.UltronCc, "5D6FEA1CB845DC5A924FDF2693DF2C80F476E5EBAAAD88565E30281447BFC3B5")]
    [InlineData(ColorPreset.Matrix, "DDDF8BAA8F7EBEF4A2B98FC8649B79F9CE1B95BF241330158DD5083F8679CB98")]
    [InlineData(ColorPreset.Infinity, "757A0700A2FB00DFBA294CBE0FAA2964F13E633CD53967C9A627163DC775F9CB")]
    [InlineData(ColorPreset.Nebulus, "BB5F6ECEF9219AEDF49144EF1D2A191CE59C61FD879ABED736962CEAFDE87451")]
    [InlineData(ColorPreset.Splendida, "47F0341ABE7AC33C1771ECF007E4D1884B9E9A63C304F5190CE91F756016F9FF")]
    public async Task PresetsKeepTheirRenderingOnTheTestPhoto(ColorPreset preset, string expectedHash)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "preset-photo.jpg");
        var image = await new ImageCodec().LoadAsync(path, CancellationToken.None);

        var output = ColorGrading.Apply(image, preset);

        Assert.Equal(image.Width, output.Width);
        Assert.Equal(image.Height, output.Height);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(output.Pixels)));
    }
}
