namespace CMPlus.Application.Abstractions;

/// <summary>
/// Reads a single Activity's progress as of a point in time using the ADR-0009 step-function
/// rule: the <c>ActivityProgressLog</c> entry with the greatest <c>PeriodEndDate</c> &lt;=
/// <paramref name="asOf"/> (see <see cref="GetProgressAsOfAsync"/>), ties broken by the greatest
/// <c>RecordedAt</c>, or 0 if no such entry exists yet. Implemented in Infrastructure against
/// <c>CmPlusDbContext</c> (S1-BE-04) - this is the Sprint 1 foundation the EVM engine's
/// project-wide reader builds on from Sprint 7 (design.md §1.1); it is deliberately scoped to a
/// single activity here rather than reusing that future interface's shape.
/// </summary>
public interface IActivityProgressReader
{
    /// <summary>Returns the progress percentage (0-100) recorded for <paramref name="activityId"/>
    /// as of <paramref name="asOf"/>, or 0 if nothing was recorded at or before that date.</summary>
    Task<decimal> GetProgressAsOfAsync(Guid activityId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
