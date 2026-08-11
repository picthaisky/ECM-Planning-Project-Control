namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (manpower-equipment) §4.7: the correction chain, identical in shape to
/// <see cref="WeatherLogEntryKind"/> (the pattern is reused verbatim, per that section's own text).
/// An <see cref="Original"/> row carries neither <c>CorrectsLogId</c> nor <c>CorrectionReason</c>; a
/// <see cref="Correction"/>/<see cref="Retraction"/> row requires both. Supersession is a forward
/// pointer only - the target row is never stamped or edited.</summary>
public enum ManpowerLogEntryKind
{
    Original = 1,
    Correction = 2,
    Retraction = 3,
}
