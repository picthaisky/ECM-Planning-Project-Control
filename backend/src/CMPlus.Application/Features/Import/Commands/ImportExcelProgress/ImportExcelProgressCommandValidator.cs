using FluentValidation;

namespace CMPlus.Application.Features.Import.Commands.ImportExcelProgress;

public sealed class ImportExcelProgressCommandValidator : AbstractValidator<ImportExcelProgressCommand>
{
    public ImportExcelProgressCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.DeclaredContentLength).GreaterThanOrEqualTo(0)
            .When(x => x.DeclaredContentLength.HasValue);
    }
}
