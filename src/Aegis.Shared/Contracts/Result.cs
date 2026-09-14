namespace Aegis.Shared.Contracts;

/// <summary>
/// Represents the result of an operation.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error != null)
            throw new System.InvalidOperationException();
        if (!isSuccess && string.IsNullOrWhiteSpace(error))
            throw new System.InvalidOperationException();

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);
}

/// <summary>
/// Represents the result of an operation with a value.
/// </summary>
public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, string? error, T? value) : base(isSuccess, error)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, null, value);
    public static new Result<T> Failure(string error) => new(false, error, default);
}
