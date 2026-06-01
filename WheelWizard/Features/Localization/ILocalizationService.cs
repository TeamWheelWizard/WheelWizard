namespace WheelWizard.Localization;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    IReadOnlyCollection<string> AvailableLanguages { get; }

    void SetLanguage(string languageCode);
    string Translate(string key);
    string TranslateForLanguage(string key, string languageCode);
    bool TryTranslateForLanguage(string key, string languageCode, out string value);
    bool HasLanguage(string languageCode);
}
