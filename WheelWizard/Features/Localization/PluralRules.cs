namespace WheelWizard.Localization;

public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

public static class PluralRules
{
    // CLDR 48 cardinal rules for the application's supported languages, for ordinary decimal counts (e = 0).
    // https://www.unicode.org/cldr/charts/48/supplemental/language_plural_rules.html
    // Decimal scale is significant: English 1 is 'one', while 1.0 is 'other'.
    public static PluralCategory Select(string language, decimal count)
    {
        var n = Math.Abs(count);
        var i = decimal.Truncate(n);
        var v = (decimal.GetBits(count)[3] >> 16) & 0xff;
        var locale = language.Replace('_', '-').ToLowerInvariant();
        var primary = locale.Split('-')[0];
        var million = v == 0 && i != 0 && i % 1_000_000 == 0;
        return primary switch
        {
            "ja" or "ko" => PluralCategory.Other,
            "fr" => i is 0 or 1 ? PluralCategory.One
            : million ? PluralCategory.Many
            : PluralCategory.Other,
            "pt" => (locale == "pt-pt" ? i == 1 && v == 0 : i is 0 or 1) ? PluralCategory.One
            : million ? PluralCategory.Many
            : PluralCategory.Other,
            "es" => n == 1 ? PluralCategory.One
            : million ? PluralCategory.Many
            : PluralCategory.Other,
            "it" => i == 1 && v == 0 ? PluralCategory.One
            : million ? PluralCategory.Many
            : PluralCategory.Other,
            "tr" => n == 1 ? PluralCategory.One : PluralCategory.Other,
            "cs" => v != 0 ? PluralCategory.Many
            : i == 1 ? PluralCategory.One
            : i is >= 2 and <= 4 ? PluralCategory.Few
            : PluralCategory.Other,
            "ru" => v != 0 ? PluralCategory.Other
            : i % 10 == 1 && i % 100 != 11 ? PluralCategory.One
            : i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? PluralCategory.Few
            : PluralCategory.Many,
            "pl" => v != 0 ? PluralCategory.Other
            : i == 1 ? PluralCategory.One
            : i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? PluralCategory.Few
            : PluralCategory.Many,
            "en" or "nl" or "de" or "fi" => i == 1 && v == 0 ? PluralCategory.One : PluralCategory.Other,
            _ => PluralCategory.Other,
        };
    }
}
