using FluentValidation;

namespace CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;

public sealed class SetEacVariantDefaultCommandValidator : AbstractValidator<SetEacVariantDefaultCommand>
{
    public SetEacVariantDefaultCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        // Defense in depth: an invalid JSON string never reaches here (System.Text.Json's globally
        // registered JsonStringEnumConverter rejects it during body deserialization, before model
        // binding completes), but a caller constructing the command directly (e.g. a future internal
        // caller, or a test) still gets an explicit rejection rather than persisting a nonsense enum
        // value - the same belt-and-braces the codebase already applies to
        // UpdateProjectCommandValidator's AdvanceRecoveryMethod.
        RuleFor(x => x.Variant).IsInEnum();
    }
}
