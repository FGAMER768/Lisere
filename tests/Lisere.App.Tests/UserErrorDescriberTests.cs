using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public sealed class UserErrorDescriberTests
{
    [Fact]
    public void LocalizedErrorsUseTheSelectedLanguage()
    {
        var strings = English();
        var error = new ImageCodecException(
            "errorImageInvalid",
            "raw decoder text");

        Assert.Equal(
            "The file is not a valid image or it is corrupted.",
            UserErrorDescriber.Describe(strings, error));
    }

    [Fact]
    public void ExplicitUserMessagesPassThrough()
    {
        var strings = English();
        var error = new UserMessageException("The video engine is unavailable.");

        Assert.Equal(
            "The video engine is unavailable.",
            UserErrorDescriber.Describe(strings, error));
    }

    [Fact]
    public void UnknownErrorsNeverLeakTheOriginalMessage()
    {
        var strings = English();
        var error = new InvalidOperationException(
            "0x80070005 E_ACCESSDENIED at line 42");

        var message = UserErrorDescriber.Describe(strings, error);

        Assert.Equal("An unexpected error occurred. Please try again.", message);
        Assert.DoesNotContain("0x80070005", message, StringComparison.Ordinal);
    }

    [Fact]
    public void InnerLocalizedErrorsAreFound()
    {
        var strings = English();
        var error = new InvalidOperationException(
            "outer",
            new PresetFormatException("errorPresetInvalidJson", "raw json"));

        Assert.Equal(
            "The preset is not a valid JSON file.",
            UserErrorDescriber.Describe(strings, error));
    }

    private static LocalizationService English()
    {
        var localization = new LocalizationService();
        localization.SetLanguage(AppLanguage.English);
        return localization;
    }
}
