using System.Windows.Media;
using Lisere.Core.Imaging;

namespace Lisere.Services;

public interface IImageCodec
{
    Task<RgbImage> LoadAsync(string path, CancellationToken cancellationToken);

    Task SaveJpegAsync(RgbImage image, string path, CancellationToken cancellationToken);

    Task<ImageSource> CreatePreviewAsync(
        RgbImage image,
        int maximumWidth,
        int maximumHeight,
        CancellationToken cancellationToken);
}
