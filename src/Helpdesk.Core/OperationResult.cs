namespace Helpdesk.Core;

/// <summary>
/// Why an operation was refused. Mapped to HTTP status codes at the edge, so the domain and service
/// layers never take a dependency on HTTP.
/// </summary>
public enum OperationError
{
    None = 0,

    /// <summary>No such record, or the caller is not entitled to know it exists.</summary>
    NotFound = 1,

    /// <summary>The caller is known but not permitted to do this.</summary>
    Forbidden = 2,

    /// <summary>The request itself is malformed or fails validation.</summary>
    Invalid = 3,

    /// <summary>The request is well formed but not legal in the record's current state.</summary>
    Conflict = 4
}

/// <summary>
/// The outcome of a service operation: a value, or a reason it could not be produced.
/// </summary>
/// <remarks>
/// Used in preference to exceptions for expected refusals. A technician trying to resolve someone
/// else's ticket is a normal event to be reported, not an exceptional one to be thrown.
/// </remarks>
public sealed class OperationResult<T>
{
    private OperationResult(bool succeeded, T? value, OperationError error, string message)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
        Message = message;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public OperationError Error { get; }

    public string Message { get; }

    public T Require() => Succeeded && Value is not null
        ? Value
        : throw new InvalidOperationException(
            $"Operation did not succeed ({Error}): {Message}");

    public static OperationResult<T> Success(T value) =>
        new(true, value, OperationError.None, string.Empty);

    public static OperationResult<T> Failure(OperationError error, string message) =>
        new(false, default, error, message);

    public static OperationResult<T> NotFound(string message = "The requested item does not exist.") =>
        Failure(OperationError.NotFound, message);

    public static OperationResult<T> Forbidden(string message) =>
        Failure(OperationError.Forbidden, message);

    public static OperationResult<T> Invalid(string message) =>
        Failure(OperationError.Invalid, message);

    public static OperationResult<T> Conflict(string message) =>
        Failure(OperationError.Conflict, message);
}
