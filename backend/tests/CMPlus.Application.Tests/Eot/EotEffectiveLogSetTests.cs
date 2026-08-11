using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>
/// ★ W-10, the half specific to S11-BE-02 (the correction-chain mechanics themselves - 405/409/422,
/// the <c>AuditLog</c> invariant - are already covered end to end by S11-BE-01's own suite; see
/// <c>EotEvaluatorTests</c>'s class remarks). Proves <see cref="EotEffectiveLogSet.Resolve"/>
/// reproduces domain-rules.md weather-eot §8.2's formula exactly across the whole
/// Original→Correction→Correction→Retraction chain, and that feeding each step's effective set
/// through <see cref="EotEvaluator"/> reproduces the fixture's own "1 → stale → 0 → 1 → 0" walk.
/// </summary>
public class EotEffectiveLogSetTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset LogDate = EotFixtures.D(2026, 7, 8);

    [Fact]
    public void Resolve_Walks_The_Whole_Original_Correction_Correction_Retraction_Chain()
    {
        var e1 = CreateOriginal(8.00m, EotFixtures.D(2026, 7, 8));
        Assert.Equal([e1], EotEffectiveLogSet.Resolve([e1]));

        var e2 = CreateCorrection(e1.Id, "ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง", 3.00m, EotFixtures.D(2026, 7, 9));
        // E1 is excluded because E2 points at it - "no SupersededBy column was written" on E1 itself.
        Assert.Equal([e2], EotEffectiveLogSet.Resolve([e1, e2]));

        var e3Prime = CreateCorrection(e2.Id, "แก้ไขอีกครั้ง", 7.00m, EotFixtures.D(2026, 7, 10));
        Assert.Equal([e3Prime], EotEffectiveLogSet.Resolve([e1, e2, e3Prime]));

        var e4 = CreateRetraction(e3Prime.Id, "บันทึกผิดวัน", EotFixtures.D(2026, 7, 11));
        // Both E3' and E4 are out - a Retraction removes itself and its target.
        Assert.Empty(EotEffectiveLogSet.Resolve([e1, e2, e3Prime, e4]));
    }

    [Fact]
    public void W10_End_To_End_Through_The_Evaluator_The_Eot_Answer_Moves_1_Then_0_Then_1_Then_0()
    {
        var run = EotFixtures.BuildN1Run(EotFixtures.D(2026, 7, 1));

        var e1 = CreateOriginal(8.00m, EotFixtures.D(2026, 7, 8));
        Assert.Equal(1, EvaluateEotEligibleDays([e1], run));

        var e2 = CreateCorrection(e1.Id, "หยุดจริง 3 ชั่วโมง", 3.00m, EotFixtures.D(2026, 7, 9));
        Assert.Equal(0, EvaluateEotEligibleDays([e1, e2], run)); // 3.00 < 4.00 MinHoursLostForCountableDay.

        var e3Prime = CreateCorrection(e2.Id, "แก้ไขอีกครั้ง", 7.00m, EotFixtures.D(2026, 7, 10));
        Assert.Equal(1, EvaluateEotEligibleDays([e1, e2, e3Prime], run));

        var e4 = CreateRetraction(e3Prime.Id, "บันทึกผิดวัน", EotFixtures.D(2026, 7, 11));
        Assert.Equal(0, EvaluateEotEligibleDays([e1, e2, e3Prime, e4], run));

        // V1 (computed against step 1's data) is never recomputed by any of this - the whole point
        // of §8.5's "a correction never mutates a stored evaluation" is exercised at the persistence
        // layer (CMPlus.Integration.Tests.Eot), not here; this test only proves the pure re-evaluation
        // arithmetic each new EotEvaluation would compute is correct at every step of the chain.
    }

    private static DailyWeatherLog CreateOriginal(decimal hoursLost, DateTimeOffset recordedAt) =>
        DailyWeatherLog.CreateOriginal(
            TenantId, ProjectId, LogDate, WeatherCondition.HeavyRain, null, null, WeatherImpact.FullStoppage, null,
            hoursLost, UserId, recordedAt, [EotFixtures.CId]);

    private static DailyWeatherLog CreateCorrection(
        Guid correctsWeatherLogId, string reason, decimal hoursLost, DateTimeOffset recordedAt) =>
        DailyWeatherLog.CreateCorrection(
            TenantId, ProjectId, correctsWeatherLogId, reason, LogDate, WeatherCondition.HeavyRain, null, null,
            WeatherImpact.FullStoppage, null, hoursLost, UserId, recordedAt, [EotFixtures.CId]);

    private static DailyWeatherLog CreateRetraction(Guid correctsWeatherLogId, string reason, DateTimeOffset recordedAt) =>
        DailyWeatherLog.CreateRetraction(
            TenantId, ProjectId, correctsWeatherLogId, reason, LogDate, WeatherCondition.HeavyRain, null, null,
            WeatherImpact.FullStoppage, null, 7.00m, UserId, recordedAt, [EotFixtures.CId]);

    private static int EvaluateEotEligibleDays(IReadOnlyList<DailyWeatherLog> allLogs, CpmRun run)
    {
        var effective = EotEffectiveLogSet.Resolve(allLogs);
        var entries = effective
            .Select(l => new EotWeatherEntryInput(
                l.Id, DateOnly.FromDateTime(l.LogDate.DateTime), l.Impact, l.HoursLost, l.RainfallMm,
                l.AffectedActivities.Select(a => a.ActivityId).ToList()))
            .ToList();

        var input = EotFixtures.BuildInput(entries, run);
        var result = EotEvaluator.Evaluate(input);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        return result.Value.EotEligibleDays;
    }
}
