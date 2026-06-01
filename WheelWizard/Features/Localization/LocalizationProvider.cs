namespace WheelWizard.Localization;

public static class LocalizationProvider
{
    private static readonly object ServiceLock = new();
    private static ILocalizationService? _service;

    public static ILocalizationService Current
    {
        get
        {
            lock (ServiceLock)
                return _service ??= new EmbeddedYamlLocalizationService();
        }
    }

    public static void Use(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (ServiceLock)
            _service = service;
    }

    public static void SetLanguage(string languageCode)
    {
        Current.SetLanguage(languageCode);
    }

    public static string Translate(string key)
    {
        return Current.Translate(key);
    }

    public static string TranslateForLanguage(string key, string languageCode)
    {
        return Current.TranslateForLanguage(key, languageCode);
    }

    public static bool TryTranslateForLanguage(string key, string languageCode, out string value)
    {
        return Current.TryTranslateForLanguage(key, languageCode, out value);
    }

    public static string GetLanguageDisplayName(string languageCode)
    {
        var language = LocalizationLanguageCatalog.Find(languageCode);
        if (language == null)
            return languageCode;

        var currentName = Translate(language.TranslationKey);
        var nativeName = TranslateForLanguage(language.TranslationKey, language.Code);

        if (
            string.IsNullOrWhiteSpace(nativeName)
            || string.Equals(nativeName, "-")
            || string.Equals(currentName, nativeName, StringComparison.Ordinal)
        )
        {
            return currentName;
        }

        return $"{currentName} - ({nativeName})";
    }
}
