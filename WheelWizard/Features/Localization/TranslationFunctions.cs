namespace WheelWizard.Localization;

public static class TranslationFunctions
{
    public static string t(string key, object? args = null)
    {
        var prefixed = TrySplitLanguageKey(key, out var language, out var translationKey);
        return Format(
            prefixed ? LocalizationProvider.TranslateForLanguage(translationKey, language) : LocalizationProvider.Translate(translationKey),
            args
        );
    }

    /// <summary>Count selects the plural form; args supplies named placeholders.</summary>
    public static string t(string key, decimal count, object? args = null)
    {
        var prefixed = TrySplitLanguageKey(key, out var language, out var translationKey);
        return Format(LocalizationProvider.Current.TranslatePlural(translationKey, count, prefixed ? language : null), args);
    }

#pragma warning disable IDE1006 // Naming Styles
    public static string t_legacy(string key, decimal count, object? args = null)
#pragma warning restore IDE1006 // Naming Styles
    {
        var hasLanguagePrefix = TrySplitLanguageKey(key, out var languageCode, out var translationKey);
        if (!hasLanguagePrefix)
            languageCode = LocalizationProvider.Current.CurrentLanguage;

        translationKey = ResolveNumberVariant(translationKey, languageCode, count);

        var translated = hasLanguagePrefix
            ? LocalizationProvider.TranslateForLanguage(translationKey, languageCode)
            : LocalizationProvider.Translate(translationKey);

        return Format(translated, args);
    }

#pragma warning disable IDE1006 // Naming Styles
    public static string tFormat(string value, object? args = null) => Format(value, args);

    public static string tTime(int seconds) => tTime(TimeSpan.FromSeconds(seconds));

    public static string tTime(long seconds) => tTime(TimeSpan.FromSeconds(seconds));

    public static string tTime(TimeSpan timeSpan)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (Math.Abs(timeSpan.TotalDays) >= 1)
        {
            var days = timeSpan.Days;
            var hours = timeSpan.Hours;
            var dayText = t_legacy("time.days.n", count: days, new { amount = days });
            if (hours == 0)
                return dayText;

            var hourText = t_legacy("time.hours.n", count: hours, new { amount = hours });
            return $"{dayText} {hourText}";
        }

        if (Math.Abs(timeSpan.TotalHours) >= 1)
        {
            var hours = timeSpan.Hours;
            var minutes = timeSpan.Minutes;
            var hourText = t_legacy("time.hours.n", count: hours, new { amount = hours });
            if (minutes == 0)
                return hourText;

            var minuteText = t_legacy("time.minutes.n", count: minutes, new { amount = minutes });
            return $"{hourText} {minuteText}";
        }

        if (Math.Abs(timeSpan.TotalMinutes) >= 1)
        {
            var minutes = timeSpan.Minutes;
            var seconds = timeSpan.Seconds;
            var minuteText = t_legacy("time.minutes.n", count: minutes, new { amount = minutes });
            if (seconds == 0)
                return minuteText;

            var secondText = t_legacy("time.seconds.n", count: seconds, new { amount = seconds });
            return $"{minuteText} {secondText}";
        }

        return t_legacy("time.seconds.n", count: timeSpan.Seconds, new { amount = timeSpan.Seconds });
    }

    private static string ResolveNumberVariant(string translationKey, string languageCode, decimal count)
    {
        if (!translationKey.EndsWith(".n", StringComparison.Ordinal))
            return translationKey;

        var countKey = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var specificKey = translationKey[..^2] + "." + countKey;
        return LocalizationProvider.TryTranslateForLanguage(specificKey, languageCode, out _) ? specificKey : translationKey;
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

    private static string Format(string value, object? args)
    {
        if (args == null)
            return value;

        var replacements = args.GetType()
            .GetProperties()
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => property.GetValue(args)?.ToString() ?? string.Empty);

        // Replace once so placeholder-like text inside an argument remains literal.
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"%\{([^{}]+)\}",
            match => replacements.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : match.Value
        );
    }
}
