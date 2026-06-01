namespace WheelWizard.Localization;

public static class TranslationFunctions
{
#pragma warning disable IDE1006 // Naming Styles
    public static string t(string key, params object?[] args)
#pragma warning restore IDE1006 // Naming Styles
    {
        var translated = TrySplitLanguageKey(key, out var languageCode, out var translationKey)
            ? LocalizationProvider.TranslateForLanguage(translationKey, languageCode)
            : LocalizationProvider.Translate(key);

        return Format(translated, args);
    }

    private static bool TrySplitLanguageKey(string key, out string languageCode, out string translationKey)
    {
        languageCode = string.Empty;
        translationKey = key;

        var separatorIndex = key.IndexOf('.');
        if (separatorIndex <= 0)
            return false;

        var maybeLanguageCode = key[..separatorIndex];
        if (!LocalizationProvider.Current.HasLanguage(maybeLanguageCode))
            return false;

        languageCode = maybeLanguageCode;
        translationKey = key[(separatorIndex + 1)..];
        return true;
    }

    private static string Format(string value, object?[] args)
    {
        for (var i = 0; i < args.Length; i++)
            value = value.Replace("{$" + (i + 1) + "}", args[i]?.ToString() ?? string.Empty, StringComparison.Ordinal);

        return value;
    }
}
