using System.Reflection;
using FluentValidation;
using MediatR;

namespace CMPlus.Application.Common;

/// <summary>
/// MediatR pipeline behavior running every registered <see cref="IValidator{T}"/> for
/// <typeparamref name="TRequest"/> before the handler (S2-BE-01, clean-architecture-dotnet skill:
/// "Validation -> Authorization/Tenant -> Audit -> Handler"). Per
/// .claude/knowledge/patterns/conventions.md ("Application returns Result&lt;T&gt; for expected
/// failures; exceptions reserved for bugs/infrastructure"), a validation failure is translated
/// into <c>Result&lt;T&gt;.Failure(...)</c> via reflection over TResponse's own static factory -
/// every command/query in this codebase returns <c>Result</c>/<c>Result&lt;T&gt;</c>, so this
/// never needs to throw in practice; the thrown fallback exists only to fail loudly if that
/// convention is ever broken for a new request type that has validators but no Result-shaped
/// response.
///
/// S4-BE-06 (gap closure): the joined message is prefixed with
/// <see cref="ValidationErrorCodes.Prefix"/> so <c>ResultProblemMapper</c> can map this failure to
/// a distinct <c>validation-error</c> RFC 7807 <c>type</c> instead of falling into its generic
/// "unmapped code" <c>bad-request</c> bucket - see that class's remarks. This does not change what
/// any validator rejects or its message text, only how the failure is tagged for the WebApi
/// mapping layer.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorList = validators.ToList();
        if (validatorList.Count == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var errorMessage = ValidationErrorCodes.Prefix + string.Join(" ", failures.Select(f => f.ErrorMessage));

        var failureFactory = typeof(TResponse).GetMethod(
            "Failure", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);

        if (failureFactory is not null)
        {
            return (TResponse)failureFactory.Invoke(null, [errorMessage])!;
        }

        throw new ValidationException(failures);
    }
}
