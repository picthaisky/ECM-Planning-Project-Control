namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §3.4/§8.1. <see cref="Entities.DailyWeatherLog.WorkStoppage"/>
/// is derived from this, never independently stored - the same discipline
/// <c>VariationOrder.Type</c> uses for the sign of <c>Amount</c>.</summary>
public enum WeatherImpact
{
    NoImpact = 0,
    PartialStoppage = 1,
    FullStoppage = 2,
}
