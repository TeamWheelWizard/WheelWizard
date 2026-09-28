using System.Globalization;
using WheelWizard.Localization;

namespace WheelWizard.Test.Features.Localization;

[Collection("SettingsFeature")]
public class PluralRulesTests
{
    [Theory]
    [InlineData("en", "1", PluralCategory.One)]
    [InlineData("en", "1.0", PluralCategory.Other)]
    [InlineData("en", "0", PluralCategory.Other)]
    [InlineData("nl", "1", PluralCategory.One)]
    [InlineData("de", "2", PluralCategory.Other)]
    [InlineData("fi", "1.5", PluralCategory.Other)]
    [InlineData("fr", "0", PluralCategory.One)]
    [InlineData("fr", "1.5", PluralCategory.One)]
    [InlineData("fr", "1000000", PluralCategory.Many)]
    [InlineData("pt", "0", PluralCategory.One)]
    [InlineData("pt-PT", "0", PluralCategory.Other)]
    [InlineData("es", "1.0", PluralCategory.One)]
    [InlineData("it", "1.0", PluralCategory.Other)]
    [InlineData("it", "1000000", PluralCategory.Many)]
    [InlineData("tr", "1.0", PluralCategory.One)]
    [InlineData("ja", "1", PluralCategory.Other)]
    [InlineData("ko", "2", PluralCategory.Other)]
    [InlineData("cs", "3", PluralCategory.Few)]
    [InlineData("cs", "1.0", PluralCategory.Many)]
    [InlineData("ru", "21", PluralCategory.One)]
    [InlineData("ru", "22", PluralCategory.Few)]
    [InlineData("ru", "12", PluralCategory.Many)]
    [InlineData("ru", "1.5", PluralCategory.Other)]
    [InlineData("ru", "-22", PluralCategory.Few)]
    [InlineData("pl", "21", PluralCategory.Many)]
    [InlineData("pl", "22", PluralCategory.Few)]
    [InlineData("pl", "12", PluralCategory.Many)]
    public void SelectsCardinalCategory(string language, string count, PluralCategory expected) =>
        Assert.Equal(expected, PluralRules.Select(language, decimal.Parse(count, CultureInfo.InvariantCulture)));

    [Fact]
    public void GlobalTranslationSupportsNamedAndPositionalCountsAndLanguageFallback()
    {
        var previous = LocalizationProvider.Current;
        var service = new EmbeddedYamlLocalizationService(typeof(PluralRulesTests).Assembly);
        LocalizationProvider.Use(service);
        try
        {
            Assert.Equal("One item for Alex", TranslationFunctions.t("items", count: 1, "Alex"));
            Assert.Equal("2 items for Alex", TranslationFunctions.t("items", 2, "Alex"));
            Assert.Equal("Value 3", TranslationFunctions.t("plain", 3));
            service.SetLanguage("ru");
            Assert.Equal("ru few", TranslationFunctions.t("items", count: 22));
            Assert.Equal("English other", TranslationFunctions.t("fallback", count: 22));
            Assert.Equal("English other", TranslationFunctions.t("en.fallback", count: 0));
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }

    [Fact]
    public void LegacyNumericKeysRemainUnchanged()
    {
        var previous = LocalizationProvider.Current;
        LocalizationProvider.Use(new EmbeddedYamlLocalizationService());
        try
        {
            Assert.Equal("1 day", TranslationFunctions.t_legacy("en.time.days.n", 1));
            Assert.Equal("2 days", TranslationFunctions.t_legacy("en.time.days.n", 2));
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }
}
