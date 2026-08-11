namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §3.4: how a partial-day stoppage becomes a countable
/// whole day. Exactly one is active per project (<see cref="Entities.ProjectEotPolicy"/>).</summary>
public enum EotPartialDayPolicy
{
    /// <summary><c>HoursLost &gt;= FullDayHours</c> counts as 1 day; nothing else counts at all.</summary>
    FullDayOnly = 1,

    /// <summary>Default. <c>HoursLost &gt;= MinHoursLostForCountableDay</c> (inclusive) counts as 1
    /// whole day.</summary>
    ThresholdWholeDay = 2,

    /// <summary>Any <c>HoursLost &gt; 0</c> accrues; the sum over an activity's countable days
    /// (within one governing run - domain-rules.md §5.1's partition) is floored by
    /// <c>FullDayHours</c> into whole days. The remainder is discarded, never carried forward.</summary>
    FractionalAccrual = 3,
}
