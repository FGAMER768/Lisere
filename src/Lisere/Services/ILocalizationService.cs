using System.ComponentModel;
using System.Globalization;
using Lisere.Models;

namespace Lisere.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    AppLanguage CurrentLanguage { get; }

    /// <summary>Culture de l'interface, pour les nombres et les durées.</summary>
    CultureInfo Culture { get; }

    event EventHandler? LanguageChanged;

    string this[string key] { get; }

    string Get(string key);

    string Format(string key, params object?[] arguments);

    void SetLanguage(AppLanguage language);
}
