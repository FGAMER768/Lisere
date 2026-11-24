namespace Lisere.Services;

public interface IFileDialogService
{
    string? PickImage();

    string? PickJpegTarget(string suggestedFileName);

    string? PickVideo();

    string? PickVideoTarget(string suggestedFileName);

    string? PickVideoPreset();

    string? PickVideoPresetTarget(string suggestedFileName);

    string? PickEnhancePreset();

    string? PickEnhancePresetTarget(string suggestedFileName);
}
