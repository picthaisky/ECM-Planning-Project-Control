using CMPlus.Domain.Enums;
using FluentValidation;

namespace CMPlus.Application.Features.Import.Commands.ImportScheduleFile;

public sealed class ImportScheduleFileCommandValidator : AbstractValidator<ImportScheduleFileCommand>
{
    public ImportScheduleFileCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Format).Must(f => f is ImportFileFormat.Xer or ImportFileFormat.Mspdi)
            .WithMessage("Format must be Xer or Mspdi.");
        RuleFor(x => x.DeclaredContentLength).GreaterThanOrEqualTo(0)
            .When(x => x.DeclaredContentLength.HasValue);
    }
}
