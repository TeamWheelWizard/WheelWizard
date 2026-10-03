namespace WheelWizard.Localization;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;
    string CurrentLanguage { get; }
    IReadOnlyCollection<string> AvailableLanguages { get; }

    void SetLanguage(string languageCode);
    string Translate(string key);
    string TranslatePlural(string key, decimal count, string? languageCode = null);
    string TranslateForLanguage(string key, string languageCode);
    bool TryTranslateForLanguage(string key, string languageCode, out string value);
    bool HasLanguage(string languageCode);
}
