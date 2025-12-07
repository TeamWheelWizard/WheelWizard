namespace WheelWizard.Shared;

/// <summary>
/// Represents the result of an operation.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public class OperationResult<T> : OperationResult
{
    private readonly T _value;

    /// <summary>
    /// The value of the operation result.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the operation was not successful.</exception>
    public T Value => IsSuccess ? _value : throw new InvalidOperationException("The operation was not successful.");

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult{T}"/> class.
    /// </summary>
    /// <param name="value">The value of the operation result.</param>
    public OperationResult(T value)
    {
        _value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult{T}"/> class with the specified error.
    /// </summary>
    /// <param name="error">The error that occurred during the operation.</param>
    public OperationResult(OperationError error)
        : base(error)
    {
        _value = default!;
    }

    #region Implicit Operators

    public static implicit operator OperationResult<T>(T value) => Ok(value);

    public static implicit operator OperationResult<T>(OperationError error) => new(error);

    public static implicit operator OperationResult<T>(Exception exception) => Fail(exception);

    #endregion
}
