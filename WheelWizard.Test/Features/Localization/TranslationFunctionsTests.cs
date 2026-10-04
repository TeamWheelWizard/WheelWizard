using WheelWizard.Localization;

namespace WheelWizard.Test.Features.Localization;

public class TranslationFunctionsTests
{
    [Fact]
    public void CountIsReservedEvenWithoutAnExplicitCountArgument()
    {
        Assert.Throws<ArgumentException>(() => TranslationFunctions.tFormat("%{count}", new { count = 99 }));
        Assert.Throws<ArgumentException>(() => TranslationFunctions.t("items", new { count = 99 }));
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
        const string value = "Hello, %{name}!";

        var result = TranslationFunctions.tFormat(value, new { name = (string?)null });

        Assert.Equal("Hello, !", result);
    }

    [Fact(DisplayName = "Format with object param returns string with object")]
    public void FormatWithObjectParam_ShouldReturnStringWithObject()
    {
        const string value = "Hello, %{name}!";

        var result = TranslationFunctions.tFormat(value, new { name = "World" });

        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void NamedArgumentsRespectTranslationOrderAndDoNotExpandInsertedValues()
    {
        var result = TranslationFunctions.tFormat(
            "%{second} / %{first} / %{second} / %{missing}",
            new { first = "%{second}", second = "Alex" }
        );

        Assert.Equal("Alex / %{second} / Alex / %{missing}", result);
    }
}
