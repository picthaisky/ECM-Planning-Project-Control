using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;

/// <summary>Response shape for a successful <see cref="CloseEvmPeriodCommand"/> - design.md §2.1's
/// `201 {snapshotId}`, extended with the frozen figures so the caller can render the just-closed
/// period without a second round trip.</summary>
public sealed record EvmSnapshotDto(
    Guid SnapshotId,
    Guid ProjectId,
    DateTimeOffset DataDate,
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    EacVariant EacVariant,
    decimal? PerformanceFactor,
    decimal? Eac,
    decimal? Etc,
    decimal? Vac,
    DateTimeOffset CreatedAt)
{
    public static EvmSnapshotDto From(EvmPeriodSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.ProjectId,
        snapshot.DataDate,
        snapshot.Bac,
        snapshot.Pv,
        snapshot.Ev,
        snapshot.Ac,
        snapshot.EacVariant,
        snapshot.PerformanceFactor,
        snapshot.Eac,
        snapshot.Etc,
        snapshot.Vac,
        snapshot.CreatedAt);
}
