namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §8.1/§8.2: the correction chain. An <see cref="Original"/>
/// row carries neither <c>CorrectsWeatherLogId</c> nor <c>CorrectionReason</c>; a
/// <see cref="Correction"/>/<see cref="Retraction"/> row requires both. Supersession is a forward
/// pointer only - the target row is never stamped or edited (§8.2).</summary>
public enum WeatherLogEntryKind
{
    Original = 1,
    Correction = 2,
    Retraction = 3,
}
