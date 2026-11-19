using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using Lisere.Models;
using Lisere.Services;

namespace Lisere.App.Tests;

public class LocalizationTests
{
    [Fact]
    public void FrenchIsTheDefaultLanguage()
    {
        var localization = new LocalizationService();

        Assert.Equal(AppLanguage.French, localization.CurrentLanguage);
        Assert.Equal("Ouvrir une image", localization["pageOpenAction"]);
    }

    [Fact]
    public void SwitchingToEnglishResolvesEnglishStrings()
    {
        var localization = new LocalizationService();

        localization.SetLanguage(AppLanguage.English);

        Assert.Equal("Open an image", localization["pageOpenAction"]);
        Assert.Equal("Settings", localization["sectionSettings"]);
    }

    [Fact]
    public void SwitchingToTheCurrentLanguageDoesNotNotify()
    {
        var localization = new LocalizationService();
        var notifications = 0;
        localization.LanguageChanged += (_, _) => notifications++;

        localization.SetLanguage(AppLanguage.French);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void SwitchingLanguageRefreshesIndexerBindings()
    {
        var localization = new LocalizationService();
        var names = new List<string?>();
        localization.PropertyChanged += (_, args) => names.Add(args.PropertyName);

        localization.SetLanguage(AppLanguage.English);

        Assert.Contains("Item[]", names);
        Assert.Contains(nameof(LocalizationService.CurrentLanguage), names);
    }

    [Fact]
    public void IndexerBindingsFollowTheSelectedLanguage()
    {
        var (french, english) = RunSta(() =>
        {
            var localization = new LocalizationService();
            var textBlock = new TextBlock
            {
                DataContext = new StringHost(localization)
            };
            BindingOperations.SetBinding(
                textBlock,
                TextBlock.TextProperty,
                new Binding("Strings[pageOpenAction]"));

            var before = textBlock.Text;
            localization.SetLanguage(AppLanguage.English);

            return (before, textBlock.Text);
        });

        Assert.Equal("Ouvrir une image", french);
        Assert.Equal("Open an image", english);
    }

    [Fact]
    public void UnknownKeysFallBackToTheKey()
    {
        var localization = new LocalizationService();

        Assert.Equal("missingKey", localization["missingKey"]);
    }

    [Theory]
    [InlineData("beforeAfterBefore", "Avant", "Before")]
    [InlineData("beforeAfterAfter", "Après", "After")]
    [InlineData("beforeAfterZoomIn", "Zoom avant", "Zoom in")]
    [InlineData("beforeAfterComparisonPosition", "Position de comparaison", "Comparison position")]
    [InlineData("stalePreviewNotice", "Aperçu obsolète : relancez l'amélioration.", "Outdated preview: run the enhancement again.")]
    [InlineData("videoProcessing", "Traitement en cours", "Processing")]
    [InlineData("errorUnexpected", "Une erreur inattendue s'est produite. Réessayez.", "An unexpected error occurred. Please try again.")]
    [InlineData("errorPresetRead", "Impossible de lire le fichier de preset.", "Could not read the preset file.")]
    [InlineData("errorPresetWrite", "Impossible d'enregistrer le preset.", "Could not save the preset.")]
    [InlineData("errorPresetAccess", "Accès refusé au fichier de preset.", "Access to the preset file was denied.")]
    [InlineData("videoErrorFileMissing", "Le fichier vidéo est introuvable.", "The video file could not be found.")]
    [InlineData("videoErrorUnreadable", "La vidéo est illisible ou son format n'est pas pris en charge.", "The video is unreadable or its format is not supported.")]
    [InlineData("videoErrorPresetInvalid", "Le preset vidéo n'est pas un fichier JSON valide.", "The video preset is not a valid JSON file.")]
    [InlineData("videoErrorPresetUnreadable", "Impossible de lire le fichier de preset vidéo.", "Could not read the video preset file.")]
    [InlineData("videoErrorRenderDetail", "Le rendu n'a pas pu aboutir. Vérifiez le fichier source et l'espace disque disponible.", "The render could not be completed. Check the source file and available disk space.")]
    [InlineData("errorImageInvalid", "Le fichier n'est pas une image valide ou il est corrompu.", "The file is not a valid image or it is corrupted.")]
    [InlineData("errorPresetVersionUnsupported", "Version de preset non prise en charge : {0}.", "Unsupported preset version: {0}.")]
    public void NewUserFacingStringsExistInBothLanguages(
        string key,
        string french,
        string english)
    {
        var frenchService = new LocalizationService();
        var englishService = new LocalizationService();
        englishService.SetLanguage(AppLanguage.English);

        Assert.Equal(french, frenchService[key]);
        Assert.Equal(english, englishService[key]);
    }

    [Fact]
    public void BothLanguagesDefineTheSameKeys()
    {
        var resources = new ResourceManager(
            "Lisere.Lang.Strings",
            typeof(LocalizationService).Assembly);

        var french = Keys(resources, CultureInfo.InvariantCulture);
        var english = Keys(resources, CultureInfo.GetCultureInfo("en"));

        Assert.Equal(french, english);
    }

    [Fact]
    public void LanguageIsRestoredFromTheStore()
    {
        var localization = new LocalizationService(
            new FakeLanguageStore(AppLanguage.English));

        Assert.Equal(AppLanguage.English, localization.CurrentLanguage);
        Assert.Equal("Open an image", localization["pageOpenAction"]);
    }

    [Fact]
    public void SwitchingLanguageIsPersisted()
    {
        var store = new FakeLanguageStore(null);
        var localization = new LocalizationService(store);

        localization.SetLanguage(AppLanguage.English);

        Assert.Equal(AppLanguage.English, store.Saved);
    }

    [Fact]
    public void LanguageStoreRoundTripsThroughAFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-settings-{Guid.NewGuid():N}.json");
        var store = new JsonLanguageStore(path);

        try
        {
            Assert.Null(store.Load());

            store.Save(AppLanguage.English);
            Assert.Equal(AppLanguage.English, store.Load());

            store.Save(AppLanguage.French);
            Assert.Equal(AppLanguage.French, store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LanguageStoreIgnoresUnreadableFiles()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "pas du json");

        try
        {
            Assert.Null(new JsonLanguageStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static T RunSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return failure is null ? result! : throw failure;
    }

    private sealed class StringHost(ILocalizationService strings)
    {
        public ILocalizationService Strings { get; } = strings;
    }

    private static string[] Keys(ResourceManager resources, CultureInfo culture) =>
        [.. resources
            .GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)];

    private sealed class FakeLanguageStore(AppLanguage? initial) : ILanguageStore
    {
        public AppLanguage? Saved { get; private set; }

        public AppLanguage? Load() => initial;

        public void Save(AppLanguage language) => Saved = language;
    }
}
