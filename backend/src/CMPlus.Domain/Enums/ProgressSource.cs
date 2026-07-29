namespace CMPlus.Domain.Enums;

/// <summary>
/// How an <c>ActivityProgressLog</c> entry was captured (backlog-detailed.md §2.2 resolution,
/// ADR-0009). Every write path (batch grid, Excel import, offline outbox replay, photo-linked
/// update) must set this on the append-only log row.
/// </summary>
public enum ProgressSource
{
    Manual = 1,
    Import = 2,
    PhotoLinked = 3,
}
