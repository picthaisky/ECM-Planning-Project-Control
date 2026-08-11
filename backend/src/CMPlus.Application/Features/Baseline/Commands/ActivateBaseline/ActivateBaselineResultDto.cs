namespace CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;

public sealed record ActivateBaselineResultDto(Guid Id, Guid ProjectId, bool IsActive);
