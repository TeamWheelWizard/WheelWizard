using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Shared;

/// <summary>
/// Represents an error that occurred during an operation.
/// </summary>
public class OperationError
{
    /// <summary>
    /// The error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The exception that occurred during the operation, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// The translation applied to this error result for better visualization in the UI.
    /// </summary>
    public MessageTranslation? MessageTranslation { get; set; }

    /// <summary>
    /// The objects to replace the keys in the translation title
    /// </summary>
    public object[]? TitleReplacements { get; set; }

    /// <summary>
    /// The objects to replace the keys in the translation extra information
    /// </summary>
    public object[]? ExtraReplacements { get; set; }

    // Note that the MessageTranslation can NOT be used to retrieve the message.
    // This is because the translation fo the MessageTranslation is localized, while the actual Message MUST be in English.

    /// <summary>
    /// Creates a new instance of the <see cref="OperationResult"/> class that indicates success.
    /// </summary>
    /// <param name="error">The error that occurred during the operation.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A new instance of the <see cref="OperationResult{T}"/> class.</returns>
    public static OperationError Fail(string error) => error;

    public static OperationError Fail(Exception error) => error;

    /// <summary>
    /// Creates a new instance of the <see cref="OperationResult"/> class that indicates success.
    /// </summary>
    /// <param name="error">The error that occurred during the operation.</param>
    /// <param name="translation">The translation for this specific error.</param>
    /// <param name="titleReplacements">The objects to replace the keys in the translation title</param>
    /// <param name="extraReplacements">The objects to replace the keys in the translation extra info</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A new instance of the <see cref="OperationResult"/> class.</returns>
    public static OperationError Fail(
        OperationError error,
        MessageTranslation? translation,
        object[]? titleReplacements = null,
        object[]? extraReplacements = null
    )
    {
        error.MessageTranslation = translation;
        error.TitleReplacements = titleReplacements;
        error.ExtraReplacements = extraReplacements;
        return error;
    }

    #region Implicit Operators

    public static implicit operator OperationError(string errorMessage) => new() { Message = errorMessage };

    public static implicit operator OperationError(Exception exception) => new() { Message = exception.Message, Exception = exception };

    #endregion
}
