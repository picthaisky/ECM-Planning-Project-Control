namespace CMPlus.Application.Services.Manpower;

/// <summary>
/// domain-rules.md (manpower-equipment) §8: equipment metrics are utilisation (hours-based) and
/// availability (count-based) - <b>never</b> a productivity index, and never combined with man-hours
/// into one denominator (fixture M-10's "0.71 instead of 0.84" negative assertion - "a project that
/// hires one extra excavator would see its productivity fall" is the tell that the sum has no
/// meaning). Deliberately its own pure static class, kept as far from
/// <see cref="ProductivityIndexCalculator"/> as <see cref="ManningRatioCalculator"/> is, for the same
/// reason: there must be no code path where an hours total silently mixes the two resource types.
/// </summary>
public static class EquipmentMetricsCalculator
{
    /// <summary>$U = EOH / (EOH + ESH) \times 100$ - the cost-relevant metric (paid standby hours
    /// produce nothing). <see langword="null"/> ("-"), never 0.00, when no hours were logged at all
    /// (§8's zero case).</summary>
    public static decimal? ComputeUtilisationPercentage(decimal equipmentOperatingHours, decimal equipmentStandbyHours)
    {
        var total = equipmentOperatingHours + equipmentStandbyHours;
        return total == 0m
            ? null
            : Math.Round(equipmentOperatingHours / total * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>$A = \textit{units operating} / \textit{units on site} \times 100$ - the prototype's
    /// "14 / 16" tile. <see langword="null"/> ("-") when no units are on site at all.</summary>
    public static decimal? ComputeAvailabilityPercentage(int unitsOperating, int unitsOnSite)
    {
        return unitsOnSite == 0
            ? null
            : Math.Round((decimal)unitsOperating / unitsOnSite * 100m, 2, MidpointRounding.AwayFromZero);
    }
}
