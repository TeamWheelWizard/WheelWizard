using System.Linq.Expressions;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.Test.Features;

public class WhWzDataTests
{
    private readonly IApiCaller<IWhWzDataApi> _apiCaller;
    private readonly WhWzDataSingletonService _service;

    public WhWzDataTests()
    {
        _apiCaller = Substitute.For<IApiCaller<IWhWzDataApi>>();
        _service = new(_apiCaller);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsStatus_WhenApiCallSucceeds()
    {
        // Arrange
        var expectedStatus = new WhWzStatus { Variant = WhWzStatusVariant.Info, Message = "Test status message" };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<WhWzStatus>>>>()).Returns(Ok(expectedStatus));

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Value);
        Assert.Equal(WhWzStatusVariant.Info, result.Value.Variant);
        Assert.Equal("Test status message", result.Value.Message);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsFailure_WhenApiCallFails()
    {
        // Arrange
        var expectedError = new OperationError { Message = "API call failed" };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<WhWzStatus>>>>()).Returns(expectedError);

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(expectedError.Message, result.Error.Message);
    }

    [Fact]
    public async Task LoadBadgesAsync_ReturnsSuccess_WhenApiCallSucceeds()
    {
        // Arrange
        var badgeData = new Dictionary<string, BadgeVariant[]>
        {
            { "FC1", [BadgeVariant.WhWzDev, BadgeVariant.Translator] },
            { "FC2", [BadgeVariant.RrDev] },
            { "FC3", [BadgeVariant.None, BadgeVariant.Firestarter_GoldWinner] },
        };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>()).Returns(Ok(badgeData));

        // Act
        var result = await _service.LoadBadgesAsync();

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task LoadBadgesAsync_ReturnsFailure_WhenApiCallFails()
    {
        // Arrange
        var expectedError = new OperationError { Message = "API call failed" };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>()).Returns(expectedError);

        // Act
        var result = await _service.LoadBadgesAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(expectedError.Message, result.Error.Message);
    }

    [Fact]
    public async Task GetBadges_ReturnsEmptyArray_WhenFriendCodeNotFound()
    {
        // Arrange
        var badgeData = new Dictionary<string, BadgeVariant[]> { { "FC1", [BadgeVariant.WhWzDev, BadgeVariant.Translator] } };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>()).Returns(Ok(badgeData));

        await _service.LoadBadgesAsync();

        // Act
        var result = _service.GetBadges("NonExistentFC");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetBadges_ReturnsBadges_WhenFriendCodeExists()
    {
        // Arrange
        var badgeData = new Dictionary<string, BadgeVariant[]> { { "FC1", [BadgeVariant.WhWzDev, BadgeVariant.Translator] } };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>()).Returns(Ok(badgeData));

        await _service.LoadBadgesAsync();

        // Act
        var result = _service.GetBadges("FC1");

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Contains(BadgeVariant.WhWzDev, result);
        Assert.Contains(BadgeVariant.Translator, result);
    }

    [Fact]
    public async Task GetBadges_FiltersOutNoneBadges_WhenLoadingBadges()
    {
        // Arrange
        var badgeData = new Dictionary<string, BadgeVariant[]> { { "FC1", [BadgeVariant.None, BadgeVariant.WhWzDev, BadgeVariant.None] } };

        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>()).Returns(Ok(badgeData));

        await _service.LoadBadgesAsync();

        // Act
        var result = _service.GetBadges("FC1");

        // Assert
        Assert.Single(result);
        Assert.Contains(BadgeVariant.WhWzDev, result);
        Assert.DoesNotContain(BadgeVariant.None, result);
    }

    [Fact]
    public async Task LoadBadgesAsync_OverwritesExistingBadges_WhenCalledMultipleTimes()
    {
        // Arrange - First load
        var initialBadgeData = new Dictionary<string, BadgeVariant[]> { { "FC1", [BadgeVariant.WhWzDev] } };

        _apiCaller
            .CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>())
            .Returns(Ok(initialBadgeData));

        await _service.LoadBadgesAsync();

        // Verify initial state
        var initialBadges = _service.GetBadges("FC1");
        Assert.Single(initialBadges);
        Assert.Contains(BadgeVariant.WhWzDev, initialBadges);

        // Arrange - Second load with different data
        var updatedBadgeData = new Dictionary<string, BadgeVariant[]> { { "FC1", [BadgeVariant.Translator, BadgeVariant.Firestarter_GoldWinner] } };

        _apiCaller
            .CallApiAsync(Arg.Any<Expression<Func<IWhWzDataApi, Task<Dictionary<string, BadgeVariant[]>>>>>())
            .Returns(Ok(updatedBadgeData));

        // Act
        await _service.LoadBadgesAsync();

        // Assert
        var updatedBadges = _service.GetBadges("FC1");
        Assert.Equal(2, updatedBadges.Length);
        Assert.Contains(BadgeVariant.Translator, updatedBadges);
        Assert.Contains(BadgeVariant.Firestarter_GoldWinner, updatedBadges);
        Assert.DoesNotContain(BadgeVariant.WhWzDev, updatedBadges);
    }
}
