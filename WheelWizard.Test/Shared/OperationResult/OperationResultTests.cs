using WheelWizard.Helpers;
using WheelWizard.Shared;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Test.Shared.OperationResultTests;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

public class OperationResultTests
{
    [Fact(DisplayName = "Create success result, should have correct state")]
    public void CreateSuccessResult_ShouldHaveCorrectState()
    {
        // Act
        var operationResult = Ok();

        // Assert
        Assert.Null(operationResult.Error);
        Assert.False(operationResult.IsFailure);
        Assert.True(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "New success result, should have correct state")]
    public void NewSuccessResult_ShouldHaveCorrectState()
    {
        // Act
        var operationResult = new OperationResult();

        // Assert
        Assert.Null(operationResult.Error);
        Assert.False(operationResult.IsFailure);
        Assert.True(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "New failure result, should have correct state")]
    public void NewFailureResult_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        var operationResult = new OperationResult(error);

        // Assert
        Assert.Equal(operationResult.Error, error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "Create failure result, should have correct state")]
    public void CreateFailureResult_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        OperationResult operationResult = error;

        // Assert
        Assert.Equal(error, operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
        Assert.Null(operationResult.Error!.MessageTranslation);
    }

    [Fact(DisplayName = "Create failure result, should have correct state with translation")]
    public void CreateFailureResult_ShouldHaveCorrectStateWithTranslation()
    {
        const string errorMessage = "Error message";
        const MessageTranslation errorTranslation = MessageTranslation.Error_StanderdError;

        // Act
        OperationResult operationResult = Fail(errorMessage, errorTranslation);

        // Assert
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
        Assert.Equal(errorMessage, operationResult.Error.Message);
        Assert.Equal(errorTranslation, operationResult.Error.MessageTranslation);
    }

    [Fact(DisplayName = "Implicit result from error, should have correct state")]
    public void ImplicitResultFromError_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        OperationResult operationResult = error;

        // Assert
        Assert.Equal(error, operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "New success generic result, should have correct state")]
    public void NewSuccessGenericResult_ShouldHaveCorrectState()
    {
        // Arrange
        var value = new object();

        // Act
        var operationResult = new OperationResult<object>(value);

        // Assert
        Assert.Null(operationResult.Error);
        Assert.False(operationResult.IsFailure);
        Assert.True(operationResult.IsSuccess);
        Assert.Equal(value, operationResult.Value);
    }

    [Fact(DisplayName = "Create success generic result, should have correct state")]
    public void CreateSuccessGenericResult_ShouldHaveCorrectState()
    {
        // Arrange
        var value = new object();

        // Act
        var operationResult = Ok(value);

        // Assert
        Assert.Null(operationResult.Error);
        Assert.False(operationResult.IsFailure);
        Assert.True(operationResult.IsSuccess);
        Assert.Equal(value, operationResult.Value);
    }

    [Fact(DisplayName = "New failure generic result, should have correct state")]
    public void NewFailureGenericResult_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        var operationResult = new OperationResult<object>(error);

        // Assert
        Assert.Equal(error, operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "Create failure generic result, should have correct state")]
    public void CreateFailureGenericResult_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        OperationResult operationResult = error;

        // Assert
        Assert.Equal(error, operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
    }

    [Fact(DisplayName = "Implicit generic result from error, should have correct state")]
    public void ImplicitGenericResultFromError_ShouldHaveCorrectState()
    {
        // Arrange
        var error = new OperationError { Message = "Error message" };

        // Act
        OperationResult<object> operationResult = error;

        // Assert
        Assert.Equal(error, operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
        Assert.Null(operationResult.Error!.MessageTranslation);
    }

    [Fact(DisplayName = "Implicit generic result from value, should have correct state")]
    public void ImplicitGenericResultFromValue_ShouldHaveCorrectState()
    {
        // Arrange
        var value = new object();

        // Act
        OperationResult<object> operationResult = value;

        // Assert
        Assert.Null(operationResult.Error);
        Assert.False(operationResult.IsFailure);
        Assert.True(operationResult.IsSuccess);
        Assert.Equal(value, operationResult.Value);
    }

    [Fact(DisplayName = "Implicit result from exception, should have failed state")]
    public void ImplicitResultFromException_ShouldHaveFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        // Act
        OperationResult operationResult = exception;

        // Assert
        Assert.NotNull(operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
        Assert.Equal(exception.Message, operationResult.Error?.Message);
        Assert.Null(operationResult.Error!.MessageTranslation);
    }

    [Fact(DisplayName = "Implicit generic result from exception, should have correct failed state")]
    public void ImplicitGenericResultFromException_ShouldHaveCorrectFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        // Act
        OperationResult<object> operationResult = exception;

        // Assert
        Assert.NotNull(operationResult.Error);
        Assert.True(operationResult.IsFailure);
        Assert.False(operationResult.IsSuccess);
        Assert.Equal(exception.Message, operationResult.Error?.Message);
    }

    [Fact(DisplayName = "Try catch without exception, should have correct success state")]
    public void TryCatchWithoutException_ShouldHaveCorrectSuccessState()
    {
        // Arrange
        void Action() { }

        // Act
        var result = TryCatch(Action);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact(DisplayName = "Try catch with exception, should have failed state")]
    public void TryCatchWithException_ShouldHaveFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        void Action() => throw exception;

        // Act
        var result = TryCatch(Action);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(exception.Message, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Try catch with exception with override, should have failed state with message")]
    public void TryCatchWithExceptionWithOverride_ShouldHaveFailedStateWithMessage()
    {
        // Arrange
        var exception = new Exception("Error message");
        const string errorMessage = "Custom error message";

        // Act
        void Action() => throw exception;

        var result = TryCatch(Action, errorMessage);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(errorMessage, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
        Assert.Null(result.Error?.MessageTranslation);
    }

    [Fact(DisplayName = "Try catch with exception with override, should have failed state with message and translation")]
    public void TryCatchWithExceptionWithOverride_ShouldHaveFailedStateWithMessageAndTranslation()
    {
        // Arrange
        var exception = new Exception("Error message");
        const string errorMessage = "Custom error message";
        const MessageTranslation errorTranslation = MessageTranslation.Error_StanderdError;

        // Act
        void Action() => throw exception;

        var result = TryCatch(Action, errorMessage, errorTranslation);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(errorMessage, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
        Assert.Equal(errorTranslation, result.Error?.MessageTranslation);
    }

    [Fact(DisplayName = "Generic try catch without exception, should have correct success state")]
    public void GenericTryCatchWithoutException_ShouldHaveCorrectSuccessState()
    {
        // Arrange
        int Func() => 42;

        // Act
        var result = TryCatch(Func);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact(DisplayName = "Generic try catch with exception, should have failed state")]
    public void GenericTryCatchWithException_ShouldHaveFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        int Func() => throw exception;

        // Act
        var result = TryCatch(Func);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(exception.Message, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Generic try catch with exception with override, should have failed state with message")]
    public void GenericTryCatchWithExceptionWithOverride_ShouldHaveFailedStateWithMessage()
    {
        // Arrange
        var exception = new Exception("Error message");
        const string errorMessage = "Custom error message";

        int Func() => throw exception;

        // Act
        var result = TryCatch(Func, errorMessage);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(errorMessage, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Generic safe execute async without exception, should have success state")]
    public async Task GenericTryCatchAsyncWithoutException_ShouldHaveSuccessState()
    {
        // Arrange
        const int expectedValue = 42;

        async Task<int> Func() => await Task.FromResult(expectedValue);

        // Act
        var result = await TryCatch(Func);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedValue, result.Value);
    }

    [Fact(DisplayName = "Generic safe execute async with exception, should have failed state")]
    public async Task GenericTryCatchAsyncWithException_ShouldHaveFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        async Task<int> Func() => throw exception;

        // Act
        var result = await TryCatch(Func);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(exception.Message, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Generic safe execute async with exception with override, should have failed state with message")]
    public async Task GenericTryCatchAsyncWithExceptionWithOverride_ShouldHaveFailedStateWithMessage()
    {
        // Arrange
        var exception = new Exception("Error message");
        const string errorMessage = "Custom error message";
        async Task<int> Func() => throw exception;

        // Act
        var result = await TryCatch(Func, errorMessage);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(errorMessage, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Try catch async without exception, should have success state")]
    public async Task TryCatchAsyncWithoutException_ShouldHaveSuccessState()
    {
        // Arrange
        async Task Func() => await Task.CompletedTask;

        // Act
        var result = await TryCatch(Func);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact(DisplayName = "Try catch async with exception, should have failed state")]
    public async Task TryCatchAsyncWithException_ShouldHaveFailedState()
    {
        // Arrange
        var exception = new Exception("Error message");

        async Task Func() => throw exception;

        // Act
        var result = await TryCatch(Func);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(exception.Message, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }

    [Fact(DisplayName = "Try catch async with exception with override, should have failed state with message")]
    public async Task TryCatchAsyncWithExceptionWithOverride_ShouldHaveFailedStateWithMessage()
    {
        // Arrange
        var exception = new Exception("Error message");
        const string errorMessage = "Custom error message";

        async Task Func() => throw exception;
        // Act
        var result = await TryCatch(Func, errorMessage);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(errorMessage, result.Error?.Message);
        Assert.Equal(exception, result.Error?.Exception);
    }
}

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
