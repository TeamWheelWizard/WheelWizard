namespace WheelWizard.Localization;

public enum PluralCategory
{
    One,
    Other,
}

public static class PluralRules
{
    // Every language uses the same simple rule, including decimal counts such as 1.0.
    public static PluralCategory Select(decimal count) => count == 1 ? PluralCategory.One : PluralCategory.Other;
}
