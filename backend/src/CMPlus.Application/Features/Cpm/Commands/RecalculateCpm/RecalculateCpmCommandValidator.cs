using FluentValidation;

namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

public sealed class RecalculateCpmCommandValidator : AbstractValidator<RecalculateCpmCommand>
{
    public RecalculateCpmCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
    }
}
