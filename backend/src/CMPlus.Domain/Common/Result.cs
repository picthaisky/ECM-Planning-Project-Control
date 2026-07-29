namespace CMPlus.Domain.Common;

/// <summary>
/// Railway-style outcome for an operation that either succeeds or fails with no return value
/// (e.g. a command handler). Exceptions remain reserved for unexpected/infrastructure failures
/// (.claude/knowledge/patterns/conventions.md) - a <see cref="DomainException"/> raised inside an
/// entity is caught at the Application boundary and turned into a <see cref="Failure"/> here,
/// never left to bubble up as a raw 500.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string Error { get; }

    protected Result(bool isSuccess, string error)
    {
        if (isSuccess && !string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException("A successful result cannot carry an error message.");
        }

        if (!isSuccess && string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("A failed result must carry a non-empty error message.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, string.Empty);

    public static Result Failure(string error) => new(false, error);
}

/// <summary>
/// Railway-style outcome carrying a success value of type <typeparamref name="T"/>. See
/// <see cref="Result"/> for the void case.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string error) : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// The success value. Throws <see cref="InvalidOperationException"/> if the result is a
    /// failure - callers must check <see cref="Result.IsSuccess"/>/<see cref="Result.IsFailure"/>
    /// first, exactly as the "expected failures never throw" convention requires.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access {nameof(Value)} of a failed result. Error: {Error}");

    public static Result<T> Success(T value) => new(true, value, string.Empty);

    public new static Result<T> Failure(string error) => new(false, default, error);

    /// <summary>Transforms the success value; a failure passes through unchanged.</summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> map) =>
        IsSuccess ? Result<TNew>.Success(map(Value)) : Result<TNew>.Failure(Error);

    /// <summary>Chains a follow-up step that itself returns a <see cref="Result{TNew}"/>; a failure passes through unchanged.</summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> bind) =>
        IsSuccess ? bind(Value) : Result<TNew>.Failure(Error);

    public static implicit operator Result<T>(T value) => Success(value);
}
