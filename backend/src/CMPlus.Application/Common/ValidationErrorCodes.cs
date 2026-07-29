namespace CMPlus.Application.Common;

/// <summary>
/// Stable marker <see cref="ValidationBehavior{TRequest,TResponse}"/> prefixes onto every
/// FluentValidation-sourced <c>Result.Failure</c> message, so <c>ResultProblemMapper</c> (WebApi)
/// can recognize "this failure came from a validator" deterministically - by a fixed, greppable
/// prefix, never by pattern-matching the (English, validator-author-written, translation-hostile)
/// message text itself. Closes the gap where a <c>UpdateProjectCommandValidator</c>/
/// <c>BatchRecordProgressCommandValidator</c>/etc. failure fell into <c>ResultProblemMapper</c>'s
/// generic "code not in the table" <c>bad-request</c> bucket, indistinguishable from any other
/// unmapped failure.
/// </summary>
public static class ValidationErrorCodes
{
    public const string Prefix = "ValidationFailed:";
}
