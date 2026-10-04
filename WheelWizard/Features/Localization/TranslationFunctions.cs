namespace WheelWizard.Localization;

public static class TranslationFunctions
{
    public static string t(string key, params object?[] args)
    {
        if (args.Length > 0 && TryCount(args[0], out var count))
            return TranslateCount(key, count, args);
        var prefixed = TrySplitLanguageKey(key, out var language, out var translationKey);
        return Format(
            prefixed ? LocalizationProvider.TranslateForLanguage(translationKey, language) : LocalizationProvider.Translate(translationKey),
            args
        );
    }

    /// <summary>Count supplies {$1}; additional arguments supply {$2}, {$3}, etc.</summary>
    public static string t(string key, decimal count, params object?[] args) => TranslateCount(key, count, [count, .. args]);

    private static string TranslateCount(string key, decimal count, object?[] args)
    {
        var prefixed = TrySplitLanguageKey(key, out var language, out var translationKey);
        return Format(LocalizationProvider.Current.TranslatePlural(translationKey, count, prefixed ? language : null), args);
    }

    private static bool TryCount(object? value, out decimal count)
    {
        count = 0;
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
            return false;
        try
        {
            count = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

#pragma warning disable IDE1006 // Naming Styles
    public static string t_legacy(string key, params object?[] args)
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

#pragma warning disable IDE1006 // Naming Styles
    public static string tFormat(string value, params object?[] args) => Format(value, args);

    public static string tTime(int seconds) => tTime(TimeSpan.FromSeconds(seconds));

    public static string tTime(long seconds) => tTime(TimeSpan.FromSeconds(seconds));

    public static string tTime(TimeSpan timeSpan)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (Math.Abs(timeSpan.TotalDays) >= 1)
        {
            var days = timeSpan.Days;
            var hours = timeSpan.Hours;
            var dayText = t_legacy("time.days.n", days);
            if (hours == 0)
                return dayText;

            var hourText = t_legacy("time.hours.n", hours);
            return $"{dayText} {hourText}";
        }

        if (Math.Abs(timeSpan.TotalHours) >= 1)
        {
            var hours = timeSpan.Hours;
            var minutes = timeSpan.Minutes;
            var hourText = t_legacy("time.hours.n", hours);
            if (minutes == 0)
                return hourText;

            var minuteText = t_legacy("time.minutes.n", minutes);
            return $"{hourText} {minuteText}";
        }

        if (Math.Abs(timeSpan.TotalMinutes) >= 1)
        {
            var minutes = timeSpan.Minutes;
            var seconds = timeSpan.Seconds;
            var minuteText = t_legacy("time.minutes.n", minutes);
            if (seconds == 0)
                return minuteText;

            var secondText = t_legacy("time.seconds.n", seconds);
            return $"{minuteText} {secondText}";
        }

        return t_legacy("time.seconds.n", timeSpan.Seconds);
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

    private static string Format(string value, object?[]? args)
    {
        if (args == null)
            return value;

        for (var i = 0; i < args.Length; i++)
            value = value.Replace("{$" + (i + 1) + "}", args[i]?.ToString() ?? string.Empty, StringComparison.Ordinal);

        return value;
    }
}
