using System.IO;
using Microsoft.Win32;

namespace Lisere.Services;

public sealed class FileDialogService : IFileDialogService
{
    internal const string ImageExtensions =
        "*.png;*.jpg;*.jpeg;*.jpe;*.jfif;*.bmp;*.dib;*.tif;*.tiff;*.gif;*.ico;*.wdp;*.jxr;*.webp;*.heic;*.heif;*.avif";

    private readonly ILocalizationService _localization;

    public FileDialogService(ILocalizationService localization) =>
        _localization = localization;

    internal string ImageFilter =>
        $"{_localization["fileFilterImages"]}|{ImageExtensions}|{_localization["fileFilterAll"]}|*.*";

    internal string JpegFilter =>
        $"{_localization["fileFilterJpeg"]}|*.jpg;*.jpeg";

    internal const string VideoExtensions =
        "*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.mts;*.m2ts;*.wmv;*.mpg;*.mpeg";

    private static readonly string[] VideoExtensionPatterns =
        VideoExtensions.Split(';', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The image picker also accepts "all files"; detect a video before handing
    /// it to the image decoder, which would only report an unreadable image.
    /// </summary>
    public static bool IsVideoPath(string path) =>
        VideoExtensionPatterns.Contains(
            "*" + Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);

    internal string VideoFilter =>
        $"{_localization["fileFilterVideos"]}|{VideoExtensions}|{_localization["fileFilterAll"]}|*.*";

    internal string Mp4Filter =>
        $"{_localization["fileFilterMp4"]}|*.mp4";

    internal string PresetFilter =>
        $"{_localization["fileFilterPresets"]}|*.json|{_localization["fileFilterAll"]}|*.*";

    internal string EnhancePresetFilter =>
        $"{_localization["fileFilterEnhancePresets"]}|*.json|{_localization["fileFilterAll"]}|*.*";

    public string? PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization["fileOpenTitle"],
            Filter = ImageFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickJpegTarget(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = _localization["fileSaveTitle"],
            Filter = JpegFilter,
            DefaultExt = ".jpg",
            FileName = suggestedFileName,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickVideo()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization["fileOpenVideoTitle"],
            Filter = VideoFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickVideoTarget(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = _localization["fileSaveVideoTitle"],
            Filter = Mp4Filter,
            DefaultExt = ".mp4",
            FileName = suggestedFileName,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickVideoPreset()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization["fileOpenPresetTitle"],
            Filter = PresetFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickEnhancePreset()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization["fileOpenEnhancePresetTitle"],
            Filter = EnhancePresetFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickEnhancePresetTarget(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = _localization["fileSaveEnhancePresetTitle"],
            Filter = EnhancePresetFilter,
            DefaultExt = ".json",
            FileName = suggestedFileName,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
