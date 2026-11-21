using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Lisere.Models;

namespace Lisere.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly ResourceManager _resources =
        new("Lisere.Lang.Strings", typeof(LocalizationService).Assembly);

    private readonly ILanguageStore? _store;
    private AppLanguage _currentLanguage;

    public LocalizationService(ILanguageStore? store = null)
    {
        _store = store;
        _currentLanguage = store?.Load() ?? AppLanguage.French;
    }

    public AppLanguage CurrentLanguage => _currentLanguage;

    public event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return _resources.GetString(key, Culture) ?? key;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Get(key), arguments);

    public void SetLanguage(AppLanguage language)
    {
        if (_currentLanguage == language)
        {
            return;
        }

        _currentLanguage = language;
        _store?.Save(language);
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public CultureInfo Culture =>
        _currentLanguage == AppLanguage.English ? EnglishCulture : FrenchCulture;
}
