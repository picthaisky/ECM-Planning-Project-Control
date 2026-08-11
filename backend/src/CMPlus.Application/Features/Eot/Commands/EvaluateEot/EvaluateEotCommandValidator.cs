using FluentValidation;

namespace CMPlus.Application.Features.Eot.Commands.EvaluateEot;

/// <summary>
/// Deliberately minimal - the window's <c>End &gt;= Start</c> cross-field check happens in the
/// handler (<see cref="EvaluateEotCommandHandler"/>), not here, mirroring
/// <c>ListWeatherLogsQueryHandler</c>'s identical precedent: <c>ValidationBehavior</c> only ever
/// surfaces a generic "validation-error" bucket, so a distinctly mapped <c>EotInvalidWindowRange</c>
/// ProblemDetails type needs a real <c>Result.Failure</c> from the handler instead.
/// </summary>
public sealed class EvaluateEotCommandValidator : AbstractValidator<EvaluateEotCommand>
{
    public EvaluateEotCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
    }
}
