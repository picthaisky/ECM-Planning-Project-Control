using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests;

/// <summary>
/// S1-QA-02 DoD (docs/10 Sprint 1 §6): normal append, backdated (out-of-order) entries,
/// <c>PeriodEndDate</c> tie-break, values outside 0-100, and immutability.
///
/// Deliberately does NOT duplicate what backend-developer already wrote (checked first, per
/// this task's brief):
/// <list type="bullet">
/// <item><see cref="CMPlus.Domain.Tests.Entities.ActivityTests"/> already covers a normal
/// append moving the cache, a single backdated entry not moving it, the
/// <c>RecordedAt</c> tie-break, <c>ActualQuantity</c> defaulting to <c>null</c> when omitted, and
/// the upper-bound percentage clamp (150 -&gt; 100).</item>
/// <item><see cref="CMPlus.Domain.Tests.Entities.ActivityProgressLogTests"/> already covers
/// immutability structurally: no public constructor, no public mutator method/property setter
/// anywhere on the type, and that <see cref="Activity.RecordProgress"/> is the only way to
/// obtain an instance.</item>
/// </list>
/// This file adds what was still missing: a genuinely-scrambled multi-period backfill sequence
/// (not just one backdated entry), the LOWER-bound clamp (negative input - not previously
/// tested, only the upper bound was), the boundary values 0 and 100 accepted as-is, and
/// <c>ActualQuantity</c>/<c>RecordedByUserId</c>/<c>Source</c> actually round-tripping onto the
/// returned entry when supplied (the existing test that passes a non-null
/// <c>ActualQuantity</c> never reads it back from the returned entry).
///
/// One naming note for whoever next greps this: <c>ActivityProgressLogTests</c> exists twice in
/// this project - this file (namespace <c>CMPlus.Domain.Tests</c>, matching the DoD's file path
/// <c>backend/tests/CMPlus.Domain.Tests/ActivityProgressLogTests.cs</c>) and
/// <c>CMPlus.Domain.Tests.Entities.ActivityProgressLogTests</c> (backend-developer's, the
/// immutability suite referenced above). Both compile and run fine since the namespaces differ,
/// but it is a real rough edge for test-explorer/`--filter` usage - flagged for
/// knowledge-curator rather than silently renamed, since renaming either file is a decision
/// that affects another agent's artifact.
/// </summary>
public class ActivityProgressLogTests
{
    private static Activity CreateActivity(decimal budgetCost = 100_000m) =>
        new(
            tenantId: Guid.NewGuid(),
            wbsNodeId: Guid.NewGuid(),
            activityCode: "A-200",
            name: "Rebar Installation",
            plannedStart: DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"),
            plannedFinish: DateTimeOffset.Parse("2026-08-01T00:00:00+07:00"),
            durationDays: 180,
            budgetCost: budgetCost);

    /// <summary>
    /// ADR-0009 / US-1.2's "out-of-order backfill" fixture, made deliberately realistic rather
    /// than a single two-entry example: five weekly periods recorded in a genuinely scrambled
    /// order (W1, W3, W5, then backfills for W2 and W4, then a same-period correction to W5)
    /// simulating a planning engineer catching up a backlog of paper progress sheets out of
    /// sequence (the exact scenario ADR-0009's "closed periods are frozen" consequence and
    /// US-3.3's Excel-import path both exist to handle). At every step, the log entry is
    /// appended (never overwritten) and <c>Activity.ProgressPercentage</c>/
    /// <c>LatestProgressPeriodEndDate</c> only ever move to a period that is the new maximum
    /// (or ties the maximum with a later <c>RecordedAt</c>) - never backwards.
    /// </summary>
    [Fact]
    public void RecordProgress_Scrambled_Multi_Period_Backfill_Only_Ever_Moves_The_Cache_Forward()
    {
        var activity = CreateActivity();
        var userId = Guid.NewGuid();

        DateTimeOffset Week(int n) => DateTimeOffset.Parse("2026-06-01T00:00:00+07:00").AddDays(7 * (n - 1));
        DateTimeOffset RecordedAt(int n, int hour = 9) => Week(n).AddDays(1).AddHours(hour);

        // W1 recorded first, on time - cache moves to W1.
        var w1 = activity.RecordProgress(Week(1), 10m, null, userId, ProgressSource.Manual, RecordedAt(1));
        Assert.Equal(10m, activity.ProgressPercentage);
        Assert.Equal(Week(1), activity.LatestProgressPeriodEndDate);

        // W3 recorded next (W2 has not arrived yet) - cache moves to W3.
        var w3 = activity.RecordProgress(Week(3), 30m, null, userId, ProgressSource.Manual, RecordedAt(3));
        Assert.Equal(30m, activity.ProgressPercentage);
        Assert.Equal(Week(3), activity.LatestProgressPeriodEndDate);

        // W5 recorded next (still no W2/W4) - cache moves to W5.
        var w5 = activity.RecordProgress(Week(5), 50m, null, userId, ProgressSource.Manual, RecordedAt(5));
        Assert.Equal(50m, activity.ProgressPercentage);
        Assert.Equal(Week(5), activity.LatestProgressPeriodEndDate);

        // The W2 paper sheet finally turns up, weeks late - appended, but W5 is still the
        // latest period, so the cache does NOT move backward to W2.
        var w2Backfill = activity.RecordProgress(Week(2), 22m, null, userId, ProgressSource.Import, RecordedAt(6));
        Assert.Equal(22m, w2Backfill.ProgressPercentage); // the entry itself holds the real value...
        Assert.Equal(50m, activity.ProgressPercentage); // ...but the cache is untouched.
        Assert.Equal(Week(5), activity.LatestProgressPeriodEndDate);

        // Same for the W4 backfill, arriving even later - still does not move the cache.
        var w4Backfill = activity.RecordProgress(Week(4), 44m, null, userId, ProgressSource.Import, RecordedAt(6, hour: 14));
        Assert.Equal(44m, w4Backfill.ProgressPercentage);
        Assert.Equal(50m, activity.ProgressPercentage);
        Assert.Equal(Week(5), activity.LatestProgressPeriodEndDate);

        // Finally, a same-PERIOD correction for W5 itself lands (e.g. a typo fix) - this is NOT
        // a backdated period (PeriodEndDate ties the current maximum), so the ADR-0009 tie-break
        // rule (greatest RecordedAt wins) applies and the cache DOES move to the corrected value.
        var w5Correction = activity.RecordProgress(Week(5), 55m, null, userId, ProgressSource.Manual, RecordedAt(6, hour: 18));
        Assert.Equal(55m, w5Correction.ProgressPercentage);
        Assert.Equal(55m, activity.ProgressPercentage);
        Assert.Equal(Week(5), activity.LatestProgressPeriodEndDate);

        // Every entry ever returned is a distinct, independently-addressable append - the
        // backfills did not reuse/mutate an earlier entry's identity or values.
        var allEntries = new[] { w1, w3, w5, w2Backfill, w4Backfill, w5Correction };
        Assert.Equal(6, allEntries.Select(e => e.Id).Distinct().Count());
        Assert.Equal(10m, w1.ProgressPercentage);
        Assert.Equal(30m, w3.ProgressPercentage);
        Assert.Equal(50m, w5.ProgressPercentage); // the original W5 entry is untouched by the later correction
    }

    /// <summary>
    /// US-1.2 DoD: <c>ProgressPercentage decimal(5,2)</c> (0.00-100.00). Per domain-decisions.md
    /// §1 ("$EV &gt; BAC$" row) and <see cref="CMPlus.Domain.Common.PercentageGuard"/>, an
    /// out-of-range input is CLAMPED at the point of assignment, not rejected with an exception
    /// - docs/10 Sprint 1 §6's DoD text ("ค่านอก 0-100 ถูกปฏิเสธ") is this clamp behaviour, not a
    /// validation throw; <see cref="CMPlus.Domain.Tests.Entities.ActivityTests.RecordProgress_Clamps_Percentage_To_0_100"/>
    /// already proves the upper bound (150 -&gt; 100) - this test adds the lower bound, which was
    /// not previously covered anywhere.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(-1000)]
    public void RecordProgress_Clamps_Negative_Percentage_To_Zero(decimal belowZero)
    {
        var activity = CreateActivity();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, belowZero, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.UtcNow);

        Assert.Equal(0m, entry.ProgressPercentage);
        Assert.Equal(0m, activity.ProgressPercentage);
    }

    /// <summary>Boundary values themselves (0 and 100) are accepted exactly as given - the clamp
    /// only alters values strictly outside the range, it must never perturb a legitimate
    /// boundary value.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void RecordProgress_Accepts_Boundary_Values_Unchanged(decimal boundary)
    {
        var activity = CreateActivity();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, boundary, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.UtcNow);

        Assert.Equal(boundary, entry.ProgressPercentage);
        Assert.Equal(boundary, activity.ProgressPercentage);
    }

    /// <summary>US-1.2 DoD: "when <c>ActualQuantity</c> is supplied, it stores as
    /// <c>decimal(18,2)</c>". The existing coverage
    /// (<see cref="CMPlus.Domain.Tests.Entities.ActivityTests.RecordProgress_ActualQuantity_Defaults_To_Null_Not_Zero"/>)
    /// only proves the omitted (null) case; this proves the supplied case actually round-trips
    /// onto the returned log entry rather than being dropped.</summary>
    [Fact]
    public void RecordProgress_ActualQuantity_Round_Trips_When_Supplied()
    {
        var activity = CreateActivity();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, 42m, actualQuantity: 875.50m, recordedByUserId: Guid.NewGuid(),
            source: ProgressSource.Manual, recordedAt: DateTimeOffset.UtcNow);

        Assert.Equal(875.50m, entry.ActualQuantity);
    }

    /// <summary>Every field passed to <see cref="Activity.RecordProgress"/> lands on the
    /// returned, persisted-shaped entry untouched - not just the ones already exercised
    /// elsewhere (<c>PeriodEndDate</c>/<c>ProgressPercentage</c>).</summary>
    [Fact]
    public void RecordProgress_Persists_RecordedByUserId_And_Source_On_The_Entry()
    {
        var activity = CreateActivity();
        var recordedByUserId = Guid.NewGuid();

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, 60m, null, recordedByUserId, ProgressSource.PhotoLinked, DateTimeOffset.UtcNow);

        Assert.Equal(recordedByUserId, entry.RecordedByUserId);
        Assert.Equal(ProgressSource.PhotoLinked, entry.Source);
        Assert.Equal(activity.Id, entry.ActivityId);
        Assert.Equal(activity.TenantId, entry.TenantId);
    }
}
