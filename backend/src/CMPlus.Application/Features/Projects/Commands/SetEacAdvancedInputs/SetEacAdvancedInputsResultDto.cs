namespace CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;

/// <summary><paramref name="EacManualEtcStaleSince"/> is surfaced so the caller can see, in the
/// same response, whether the write it just made also cleared a pre-existing staleness flag
/// (<c>Project.SetEacManualEtc</c> always clears it, domain-rules.md §5.7) - always
/// <see langword="null"/> immediately after a successful call, but returning it here (rather than
/// nothing) makes that guarantee visible to any caller/test without a second round trip.</summary>
public sealed record SetEacAdvancedInputsResultDto(
    Guid ProjectId,
    decimal? EacManualEtc,
    decimal? EacCustomPerformanceFactor,
    DateTimeOffset? EacManualEtcStaleSince);
