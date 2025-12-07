using System.Diagnostics.CodeAnalysis;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Shared;

/// <summary>
/// Represents the result of an operation.
/// </summary>
public class OperationResult
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public virtual bool IsSuccess => Error is null;

    /// <summary>
    /// Indicates whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public virtual bool IsFailure => !IsSuccess;

    /// <summary>
    /// The error that occurred during the operation, if any.
    /// </summary>
    /// <remarks>
    /// This property is <see langword="null"/> if the operation was successful.
    /// </remarks>
    public OperationError? Error { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult"/> class.
    /// </summary>
    public OperationResult() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult"/> class with the specified error.
    /// </summary>
    /// <param name="error">The error that occurred during the operation.</param>
    public OperationResult(OperationError error)
    {
        Error = error;
    }

    #region Creation Methods

    /// <summary>
    /// Creates a new instance of the <see cref="OperationResult"/> class that indicates success.
    /// </summary>
    /// <returns>A new instance of the <see cref="OperationResult"/> class.</returns>
    public static OperationResult Ok() => new();

    /// <summary>
    /// Creates a new instance of the <see cref="OperationResult{T}"/> class that indicates success.
    /// </summary>
    /// <param name="value">The value of the operation result.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A new instance of the <see cref="OperationResult{T}"/> class.</returns>
    public static OperationResult<T> Ok<T>(T value) => new(value);

    /// <summary>
    /// Executes the specified function and returns the result.
    /// Catches any exceptions thrown by the function and returns a failure result.
    /// </summary>
    /// <param name="func">The function to execute.</param>
    /// <param name="errorMessage">The error message to return if the function fails.</param>
    /// <param name="translation">The translation for this specific error.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An <see cref="OperationResult{T}"/> that indicates the result of the operation.</returns>
    public static OperationResult<T> TryCatch<T>(Func<T> func, string? errorMessage = null, MessageTranslation? translation = null)
    {
        try
        {
            var value = func();
            return Ok(value);
        }
        catch (Exception ex)
        {
            return new OperationError()
            {
                Message = errorMessage ?? ex.Message,
                Exception = ex,
                MessageTranslation = translation,
                TitleReplacements = [ex.Message],
                ExtraReplacements = [ex.Message],
            };
        }
    }

    /// <summary>
    /// Executes the specified function and returns the result.
    /// Catches any exceptions thrown by the function and returns a failure result.
    /// </summary>
    /// <param name="func">The function to execute.</param>
    /// <param name="errorMessage">The error message to return if the function fails.</param>
    /// <param name="translation">The translation for this specific error.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An <see cref="OperationResult{T}"/> that indicates the result of the operation.</returns>
    public static async Task<OperationResult<T>> TryCatch<T>(
        Func<Task<T>> func,
        string? errorMessage = null,
        MessageTranslation? translation = null
    )
    {
        try
        {
            var value = await func();
            return Ok(value);
        }
        catch (Exception ex)
        {
            return new OperationError()
            {
                Message = errorMessage ?? ex.Message,
                Exception = ex,
                MessageTranslation = translation,
                TitleReplacements = [ex.Message],
                ExtraReplacements = [ex.Message],
            };
        }
    }

    /// <summary>
    /// Executes the specified function and returns the result.
    /// Catches any exceptions thrown by the function and returns a failure result.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="errorMessage">The error message to return if the function fails.</param>
    /// <param name="translation">The translation for this specific error.</param>
    /// <returns>An <see cref="OperationResult"/> that indicates the result of the operation.</returns>
    public static OperationResult TryCatch(Action action, string? errorMessage = null, MessageTranslation? translation = null)
    {
        try
        {
            action();
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError()
            {
                Message = errorMessage ?? ex.Message,
                Exception = ex,
                MessageTranslation = translation,
                TitleReplacements = [ex.Message],
                ExtraReplacements = [ex.Message],
            };
        }
    }

    /// <summary>
    /// Executes the specified function and returns the result.
    /// Catches any exceptions thrown by the function and returns a failure result.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="errorMessage">The error message to return if the function fails.</param>
    /// <param name="translation">The translation for this specific error.</param>
    /// <returns>An <see cref="OperationResult"/> that indicates the result of the operation.</returns>
    public static async Task<OperationResult> TryCatch(
        Func<Task> action,
        string? errorMessage = null,
        MessageTranslation? translation = null
    )
    {
        try
        {
            await action();
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError()
            {
                Message = errorMessage ?? ex.Message,
                Exception = ex,
                MessageTranslation = translation,
                TitleReplacements = [ex.Message],
                ExtraReplacements = [ex.Message],
            };
        }
    }

    #endregion

    #region Implicit Operators

    public static implicit operator OperationResult(OperationError error) => new(error);

    public static implicit operator OperationResult(Exception exception) => Fail(exception);

    #endregion
}
