using System.ComponentModel;
using Lisere.Models;

namespace Lisere.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    AppLanguage CurrentLanguage { get; }

    event EventHandler? LanguageChanged;

    string this[string key] { get; }

    string Get(string key);

    string Format(string key, params object?[] arguments);

    void SetLanguage(AppLanguage language);
}
