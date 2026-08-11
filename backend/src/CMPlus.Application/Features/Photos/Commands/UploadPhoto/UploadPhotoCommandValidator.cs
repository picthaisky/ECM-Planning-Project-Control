using FluentValidation;

namespace CMPlus.Application.Features.Photos.Commands.UploadPhoto;

public sealed class UploadPhotoCommandValidator : AbstractValidator<UploadPhotoCommand>
{
    public UploadPhotoCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.ActivityId).NotEqual(Guid.Empty).When(x => x.ActivityId.HasValue);
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.DeclaredContentLength).GreaterThanOrEqualTo(0).When(x => x.DeclaredContentLength.HasValue);
    }
}
