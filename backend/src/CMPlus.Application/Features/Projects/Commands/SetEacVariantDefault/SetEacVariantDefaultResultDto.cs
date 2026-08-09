using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;

public sealed record SetEacVariantDefaultResultDto(Guid ProjectId, EacVariant EacVariantDefault);
