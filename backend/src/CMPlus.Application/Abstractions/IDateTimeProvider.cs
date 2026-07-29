namespace CMPlus.Application.Abstractions;

/// <summary>
/// Testable clock abstraction. Infrastructure implements this by returning
/// <see cref="DateTimeOffset.UtcNow"/>; tests substitute a fixed instant so time-dependent
/// behaviour (e.g. <c>ActivityProgressLog.RecordedAt</c>, audit timestamps) is deterministic.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
