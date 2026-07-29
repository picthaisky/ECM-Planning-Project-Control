namespace CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;

/// <summary>S4-BE-03 DoD: "audit 1 entry per batch with a count summary" - this is that same
/// summary, echoed back to the caller.</summary>
public sealed record BatchRecordProgressResultDto(int EntriesRecorded);
