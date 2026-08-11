using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Eot;

/// <summary>
/// domain-rules.md weather-eot §8.2: $\mathcal{L}^{eff}$, computed at read time, never stored - "an
/// entry is in force iff nothing points at it and it is not itself a Retraction. A Retraction
/// therefore removes both itself and its target from $\mathcal{L}^{eff}$". Pure over an
/// already-loaded set of <see cref="DailyWeatherLog"/> rows (typically every row for a project - a
/// correction chain is not date-scoped, so determining whether an in-window entry has since been
/// superseded needs the whole project's history, not just the window). The read side
/// <c>EvaluateEotCommandHandler</c> calls before building <see cref="EotEvaluator"/>'s input.
/// </summary>
public static class EotEffectiveLogSet
{
    public static IReadOnlyList<DailyWeatherLog> Resolve(IReadOnlyList<DailyWeatherLog> allLogs)
    {
        var pointedAt = allLogs
            .Where(l => l.CorrectsWeatherLogId is not null)
            .Select(l => l.CorrectsWeatherLogId!.Value)
            .ToHashSet();

        return allLogs
            .Where(l => l.EntryKind != WeatherLogEntryKind.Retraction && !pointedAt.Contains(l.Id))
            .ToList();
    }
}
