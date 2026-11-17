using System.IO;
using System.Text.Json;
using Lisere.Models;

namespace Lisere.Services;

public interface ILanguageStore
{
    AppLanguage? Load();

    void Save(AppLanguage language);
}

public sealed class JsonLanguageStore : ILanguageStore
{
    private readonly string _path;

    public JsonLanguageStore(string? path = null) =>
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lisere",
            "settings.json");

    public AppLanguage? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path));

            return settings?.Language switch
            {
                "en" => AppLanguage.English,
                "fr" => AppLanguage.French,
                _ => null
            };
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(AppLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new Settings
            {
                Language = language == AppLanguage.English ? "en" : "fr"
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Settings
    {
        public string? Language { get; init; }
    }
}
