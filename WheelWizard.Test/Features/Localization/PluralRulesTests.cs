using System.Globalization;
using WheelWizard.Localization;

namespace WheelWizard.Test.Features.Localization;

[Collection("SettingsFeature")]
public class PluralRulesTests
{
    [Theory]
    [InlineData("1", PluralCategory.One)]
    [InlineData("1.0", PluralCategory.One)]
    [InlineData("0", PluralCategory.Other)]
    [InlineData("2", PluralCategory.Other)]
    [InlineData("1.5", PluralCategory.Other)]
    [InlineData("-1", PluralCategory.Other)]
    [InlineData("21", PluralCategory.Other)]
    public void SelectsSimpleCategory(string count, PluralCategory expected) =>
        Assert.Equal(expected, PluralRules.Select(decimal.Parse(count, CultureInfo.InvariantCulture)));

    [Fact]
    public void GlobalTranslationSupportsNamedAndPositionalCountsAndLanguageFallback()
    {
        var previous = LocalizationProvider.Current;
        var service = new EmbeddedYamlLocalizationService(typeof(PluralRulesTests).Assembly);
        LocalizationProvider.Use(service);
        try
        {
            Assert.Equal("One item for Alex", TranslationFunctions.t("items", count: 1, new { name = "Alex" }));
            Assert.Equal("2 items for Alex", TranslationFunctions.t("items", count: 2, new { amount = 2, name = "Alex" }));
            Assert.Equal("Value 3", TranslationFunctions.t("plain", new { amount = 3 }));
            service.SetLanguage("ru");
            Assert.Equal("ru other", TranslationFunctions.t("items", count: 22));
            Assert.Equal("English other", TranslationFunctions.t("fallback", count: 22));
            Assert.Equal("English other", TranslationFunctions.t("en.fallback", count: 0));
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }

    [Theory]
    [InlineData("pt", "pt other")]
    [InlineData("pt-BR", "pt other")]
    [InlineData("pt-PT", "pt other")]
    [InlineData("PT_pt", "pt other")]
    public void GlobalTranslationUsesTheSameRuleForEveryRegion(string locale, string expected)
    {
        var previous = LocalizationProvider.Current;
        var service = new EmbeddedYamlLocalizationService(typeof(PluralRulesTests).Assembly);
        LocalizationProvider.Use(service);
        try
        {
            Assert.Equal(expected, TranslationFunctions.t($"{locale}.items", count: 0));
            Assert.Equal("English other", TranslationFunctions.t($"{locale}.fallback", count: 0));
            Assert.Equal("pt one", TranslationFunctions.t($"{locale}.items", count: 1));
            service.SetLanguage(locale);
            Assert.Equal(expected, TranslationFunctions.t("items", count: 0));
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }

    [Fact]
    public void TimeTranslationsUseSimpleCardinalForms()
    {
        var previous = LocalizationProvider.Current;
        LocalizationProvider.Use(new EmbeddedYamlLocalizationService());
        try
        {
            Assert.Equal("1 day", TranslationFunctions.t("en.time.days", 1));
            Assert.Equal("2 days", TranslationFunctions.t("en.time.days", count: 2, new { amount = 2 }));
            Assert.Equal("0 seconds", TranslationFunctions.tTime(0));
            Assert.Equal("1 minute", TranslationFunctions.tTime(60));
            Assert.Equal("1 minute 2 seconds", TranslationFunctions.tTime(62));
            foreach (var key in new[] { "players_online", "friends_online", "rooms_online" })
            {
                var translationKey = $"en.hover.{key}";
                var emptyText = TranslationFunctions.t($"{translationKey}.none");
                var zeroCountText = TranslationFunctions.t(translationKey, count: 0, new { amount = 0 });
                Assert.DoesNotContain(".none", emptyText);
                Assert.NotEqual(emptyText, zeroCountText);
                Assert.Contains("0", zeroCountText);
            }
        }
        finally
        {
            LocalizationProvider.Use(previous);
        }
    }
}
