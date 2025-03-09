using WheelWizard.Helpers;

namespace WheelWizard.Test.Helpers;

public class HumanizerTests
{
    [Fact(DisplayName = "Replace dynamic with no params, should return default string")]
    public void ReplaceDynamicWithNoParams_ShouldReturnDefaultString()
    {
        // Arrange
        const string langString = "Hello, World!";

        // Act
        var result = Humanizer.ReplaceDynamic(langString);

        // Assert
        Assert.Equal(langString, result);
    }

    [Fact(DisplayName = "Replace dynamic with null object param, should return string with null")]
    public void ReplaceDynamicWithNullObjectParam_ShouldReturnStringWithNull()
    {
        // Arrange
        const string langString = "Hello, {$1}!";

        // Act
        var result = Humanizer.ReplaceDynamic(langString, [null!]);

        // Assert
        Assert.Equal("Hello, !", result);
    }

    [Fact(DisplayName = "Replace dynamic with object param, should return string with object")]
    public void ReplaceDynamicWithObjectParam_ShouldReturnStringWithObject()
    {
        // Arrange
        const string langString = "Hello, {$1}!";

        // Act
        var result = Humanizer.ReplaceDynamic(langString, "World");

        // Assert
        Assert.Equal("Hello, World!", result);
    }
}
