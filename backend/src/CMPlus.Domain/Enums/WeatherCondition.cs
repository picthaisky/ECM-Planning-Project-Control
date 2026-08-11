namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §8.1: closed vocabulary for
/// <see cref="Entities.DailyWeatherLog.Condition"/> - free-text detail belongs in
/// <c>ConditionNote</c> instead.</summary>
public enum WeatherCondition
{
    Clear = 1,
    Cloudy = 2,
    LightRain = 3,
    ModerateRain = 4,
    HeavyRain = 5,
    Storm = 6,
    Flood = 7,
    Other = 8,
}
