namespace CMPlus.Application.Features.Weather;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for S11-BE-01's
/// <c>RecordWeatherLogCommand</c>/<c>RecordWeatherLogCorrectionCommand</c>/<c>ListWeatherLogsQuery</c>
/// (domain-rules.md weather-eot §8).</summary>
public static class WeatherLogErrorCodes
{
    public const string ProjectNotFound = "WeatherLogProjectNotFound";

    /// <summary>Sprint 10's L-01 fix pattern (this task's brief): the caller has no resolvable user
    /// id (<c>ICurrentUserContext.UserId</c> is null). Structurally unreachable behind
    /// <c>[Authorize]</c> in production; kept as a fail-closed guard rather than ever stamping
    /// <c>Guid.Empty</c> onto <c>RecordedByUserId</c>. Maps to 401.</summary>
    public const string ActorRequired = "WeatherLogActorRequired";

    /// <summary>One or more <c>AffectedActivityIds</c> values do not belong to this project. Maps to 400.</summary>
    public const string UnknownActivity = "WeatherLogUnknownActivity";

    /// <summary><c>ListWeatherLogsQuery</c>'s <c>from</c> is later than <c>to</c>. Maps to 400.</summary>
    public const string InvalidDateRange = "WeatherLogInvalidDateRange";

    /// <summary>domain-rules.md §8.2 chain-integrity rule 1: <c>CorrectsWeatherLogId</c> (taken from
    /// the route, never the body) does not resolve to an existing <see cref="CMPlus.Domain.Entities.DailyWeatherLog"/>
    /// in this project. Maps to 422.</summary>
    public const string CorrectionTargetNotFound = "WeatherLogCorrectionTargetNotFound";

    /// <summary>domain-rules.md §8.2 chain-integrity rule 2 (the load-bearing one): the target
    /// already has an entry pointing at it - a correction must target the current chain tail, not an
    /// already-superseded entry. Maps to 409.</summary>
    public const string AlreadySuperseded = "WeatherLogAlreadySuperseded";

    /// <summary>domain-rules.md §8.2 chain-integrity rule 4: the target must be strictly older
    /// (<c>target.RecordedAt &lt; this.RecordedAt</c>). Maps to 422.</summary>
    public const string CorrectionOrdering = "WeatherLogCorrectionOrdering";

    /// <summary>domain-rules.md §8.3: there is no <c>PUT</c>/<c>PATCH</c>/<c>DELETE</c> route for a
    /// weather log - a deliberate 405 pointing the caller at the corrections endpoint, rather than a
    /// bare routing 404/405 with no guidance. Maps to 405.</summary>
    public const string IsImmutable = "WeatherLogIsImmutable";
}
