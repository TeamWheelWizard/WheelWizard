using WheelWizard.Localization;

namespace WheelWizard.Test.Features.Localization;

[Collection("SettingsFeature")]
public class TranslationFunctionsTests
{
    [Fact]
    public void ProviderForwardsOnlyActiveServiceChangesOnce()
    {
        var previous = LocalizationProvider.Current;
        var first = new EmbeddedYamlLocalizationService();
        var second = new EmbeddedYamlLocalizationService();
        var notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        LocalizationProvider.Use(first);
        LocalizationProvider.LanguageChanged += handler;
        try
        {
            first.SetLanguage("nl");
            first.SetLanguage("NL");
            Assert.Equal(1, notifications);
            LocalizationProvider.Use(second);
            Assert.Equal(2, notifications);
            first.SetLanguage("fr");
            Assert.Equal(2, notifications);
            second.SetLanguage("fr");
            Assert.Equal(3, notifications);
            Assert.Equal(second.Translate("action.cancel"), TranslationFunctions.t("action.cancel"));
        }
        finally
        {
            LocalizationProvider.LanguageChanged -= handler;
            LocalizationProvider.Use(previous);
        }
    }

    [Fact(DisplayName = "Format with no params returns default string")]
    public void FormatWithNoParams_ShouldReturnDefaultString()
    {
        const string value = "Hello, World!";

        var result = TranslationFunctions.tFormat(value);

        Assert.Equal(value, result);
    }

    [Fact(DisplayName = "Format with null object param returns string with empty value")]
    public void FormatWithNullObjectParam_ShouldReturnStringWithEmptyValue()
    {
        const string value = "Hello, {$1}!";

        var result = TranslationFunctions.tFormat(value, [null]);

        Assert.Equal("Hello, !", result);
    }

    [Fact(DisplayName = "Format with object param returns string with object")]
    public void FormatWithObjectParam_ShouldReturnStringWithObject()
    {
        const string value = "Hello, {$1}!";

        var result = TranslationFunctions.tFormat(value, "World");

        Assert.Equal("Hello, World!", result);
    }
}
