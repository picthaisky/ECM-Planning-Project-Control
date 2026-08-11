namespace CMPlus.Application.Services.Manpower;

/// <summary>
/// domain-rules.md (manpower-equipment) §5.1: <c>ManCount / PlannedManCount</c> - the manning ratio,
/// deliberately a completely separate pure function from <see cref="ProductivityIndexCalculator"/>
/// so the two identifiers can never be accidentally unified into one code path (fixture M-02's own
/// assertion: "no field named <c>productivityIndex</c> ever equals 1.25 for this input").
///
/// <para><b>Not a performance index.</b> §5.1: "a headcount ratio has no output term at all... it
/// also has no agreed direction". This engine intentionally computes nothing about progress,
/// budgeted hours, or direction - it is a bare staffing-compliance ratio and must never be
/// colour-coded good/bad the way <see cref="ProductivityIndexCalculator"/>'s result is (§5.3's
/// "neutral variance palette" ruling; §9.2's "+30 คน over plan renders green" defect this separation
/// exists to prevent).</para>
/// </summary>
public static class ManningRatioCalculator
{
    /// <summary><see langword="null"/> - never 0 - when <paramref name="plannedWorkerCount"/> is
    /// unset (no manning plan for this scope/date, ManpowerPlan.PlannedWorkerCount is null) or is
    /// exactly 0 (division guard; a planned headcount of zero has no meaningful ratio either).</summary>
    public static ManningRatioResult Compute(int actualWorkerCount, int? plannedWorkerCount)
    {
        if (plannedWorkerCount is null or 0)
        {
            return new ManningRatioResult(null, actualWorkerCount, plannedWorkerCount);
        }

        var value = Math.Round((decimal)actualWorkerCount / plannedWorkerCount.Value, 2, MidpointRounding.AwayFromZero);
        return new ManningRatioResult(value, actualWorkerCount, plannedWorkerCount);
    }
}
