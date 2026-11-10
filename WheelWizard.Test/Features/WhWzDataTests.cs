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
}
