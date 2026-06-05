using WheelWizard.Localization;

namespace WheelWizard.Test.Features.Localization;

public class TranslationFunctionsTests
{
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
