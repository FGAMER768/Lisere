namespace Lisere.Services;

public interface IFileDialogService
{
    string? PickImage();

    string? PickJpegTarget(string suggestedFileName);

    string? PickVideo();

    string? PickVideoTarget(string suggestedFileName);

    string? PickVideoPreset();

    string? PickEnhancePreset();

    string? PickEnhancePresetTarget(string suggestedFileName);
}
