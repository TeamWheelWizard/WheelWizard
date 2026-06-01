namespace WheelWizard.Localization;

public static class TranslationFunctions
{
#pragma warning disable IDE1006 // Naming Styles
    public static string t(string key, params object?[] args)
#pragma warning restore IDE1006 // Naming Styles
    {
        var hasLanguagePrefix = TrySplitLanguageKey(key, out var languageCode, out var translationKey);
        if (!hasLanguagePrefix)
            languageCode = LocalizationProvider.Current.CurrentLanguage;

        translationKey = ResolveNumberVariant(translationKey, languageCode, args);

        var translated = hasLanguagePrefix
            ? LocalizationProvider.TranslateForLanguage(translationKey, languageCode)
            : LocalizationProvider.Translate(translationKey);

        return Format(translated, args);
    }

    private static string ResolveNumberVariant(string translationKey, string languageCode, object?[] args)
    {
        if (!translationKey.EndsWith(".n", StringComparison.Ordinal) || args.Length == 0)
            return translationKey;

        var countKey = ToCountKey(args[0]);
        if (string.IsNullOrWhiteSpace(countKey))
            return translationKey;

        var specificKey = translationKey[..^2] + "." + countKey;
        return LocalizationProvider.TryTranslateForLanguage(specificKey, languageCode, out _) ? specificKey : translationKey;
    }

    private static string? ToCountKey(object? value)
    {
        return value switch
        {
            null => null,
            sbyte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            byte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
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
