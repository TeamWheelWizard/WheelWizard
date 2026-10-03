namespace WheelWizard.Localization;

public static class LocalizationProvider
{
    // Compatibility facade for global t() and XAML; the injected service owns the language.
    private static readonly object ServiceLock = new();
    private static ILocalizationService? _service;

    public static event EventHandler? LanguageChanged;

    public static ILocalizationService Current
    {
        get
        {
            lock (ServiceLock)
            {
                if (_service == null)
                {
                    _service = new EmbeddedYamlLocalizationService();
                    _service.LanguageChanged += ForwardLanguageChanged;
                }
                return _service;
            }
        }
    }

    public static void Use(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (ServiceLock)
        {
            if (ReferenceEquals(_service, service))
                return;
            if (_service != null)
                _service.LanguageChanged -= ForwardLanguageChanged;
            _service = service;
            _service.LanguageChanged += ForwardLanguageChanged;
        }

        NotifyLanguageChanged();
    }

    public static void SetLanguage(string languageCode)
    {
        Current.SetLanguage(languageCode);
    }

    private static void ForwardLanguageChanged(object? sender, EventArgs args) => NotifyLanguageChanged();

    private static void NotifyLanguageChanged()
    {
        LanguageChanged?.Invoke(null, EventArgs.Empty);
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
