using WheelWizard.Localization.Domain;

namespace WheelWizard.Localization;

public static class LocalizationLanguageCatalog
{
    public static IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        [
            new("en", "value.language.english"),
            new("nl", "value.language.dutch"),
            new("fr", "value.language.france"),
            new("de", "value.language.german"),
            new("fi", "value.language.finnish"),
            new("cs", "value.language.czech"),
            new("ja", "value.language.japanese"),
            new("es", "value.language.spanish"),
            new("it", "value.language.italian"),
            new("pt", "value.language.portuguese"),
            new("ru", "value.language.russian"),
            new("ko", "value.language.korean"),
            new("tr", "value.language.turkish"),
        ];

    public static LanguageInfo? Find(string languageCode)
    {
        return SupportedLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase)
        );
    }
}
