using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S8 (actual-cost.md §7.1/§8, ADR-0013): <see cref="ActualCostReader"/> against a real
/// <see cref="CmPlusDbContext"/> - the backend-developer sanity-check suite transcribing the
/// domain-expert-authored AC-1/AC-2/AC-5/AC-6 fixtures (actual-cost.md §8), plus the negative-AC
/// and project-scoping rules from §7.6/§9 that have no numbered fixture of their own. Uses the EF
/// Core InMemory provider (Docker Desktop cannot start on this workstation - see the
/// backend-developer report); does not verify the real SQL Server index-seek plan (that remains a
/// database-engineer concern once the Docker outage is resolved).
/// </summary>
public class ActualCostReaderTests
{
    private static ActualCostEntry Entry(
        Guid tenantId,
        Guid projectId,
        decimal amount,
        DateTimeOffset incurredDate,
        DateTimeOffset postedAt,
        ActualCostEntryType entryType = ActualCostEntryType.Actual,
        Guid? reversesEntryId = null) =>
        new(
            tenantId, projectId, wbsNodeId: null, activityId: null,
            CostCategory.Subcontract, entryType, ActualCostSource.ManualEntry,
            amount, incurredDate, postedAt, Guid.NewGuid(), reversesEntryId,
            documentReference: null, costCode: null, vendorName: null, note: null,
            fileImportJobId: null, paidDate: null, quantity: null, unitOfMeasure: null);

    [Fact]
    public async Task GetActualCostAsOfAsync_Returns_Zero_And_Zero_Count_When_No_Entries_Exist()
    {
        // AC-5 (actual-cost.md §8): no entries -> AC = 0.00 (not null), entry count 0.
        var factory = new TestDbContextFactory(Guid.NewGuid());
        using var context = factory.CreateContext();
        var reader = new ActualCostReader(context);

        var result = await reader.GetActualCostAsOfAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(0m, result.Amount);
        Assert.Equal(0, result.EntryCount);
    }

    [Fact]
    public async Task GetActualCostAsOfAsync_Nets_An_Accrual_Reversal_To_Zero_But_Still_Reports_A_Nonzero_Entry_Count()
    {
        // AC-6 (actual-cost.md §8): accrual +250,000.00 and its reversal -250,000.00, invoice not
        // yet posted -> AC = 0.00, entry count 2. This is the exact case ActualCostResult's own
        // EntryCount exists to distinguish from AC-5's "nothing recorded yet" (both would otherwise
        // read as an identical, indistinguishable 0.00).
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var t = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ActualCostEntries.AddRange(
                Entry(tenantId, projectId, 250_000.00m, t, t.AddDays(8), ActualCostEntryType.Accrual),
                Entry(tenantId, projectId, -250_000.00m, t, t.AddDays(20), ActualCostEntryType.AccrualReversal));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ActualCostReader(readContext);

        var result = await reader.GetActualCostAsOfAsync(projectId, t);

        Assert.Equal(0.00m, result.Amount);
        Assert.Equal(2, result.EntryCount);
    }

    [Fact]
    public async Task GetActualCostAsOfAsync_Returns_A_Negative_Amount_As_Is_When_A_Reversal_Exceeds_Recorded_Cost()
    {
        // actual-cost.md §7.6: AC(t) < 0 (over-reversal/credit note exceeding costs) - compute and
        // return as-is, never clamp.
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var t = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ActualCostEntries.AddRange(
                Entry(tenantId, projectId, 100_000.00m, t, t, ActualCostEntryType.Actual),
                Entry(tenantId, projectId, -150_000.00m, t, t, ActualCostEntryType.Adjustment));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ActualCostReader(readContext);

        var result = await reader.GetActualCostAsOfAsync(projectId, t);

        Assert.Equal(-50_000.00m, result.Amount);
        Assert.Equal(2, result.EntryCount);
    }

    [Fact]
    public async Task GetActualCostAsOfAsync_Never_Includes_Another_Projects_Entries_Even_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var t = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ActualCostEntries.AddRange(
                Entry(tenantId, projectA, 100_000.00m, t, t),
                Entry(tenantId, projectB, 999_999.00m, t, t));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ActualCostReader(readContext);

        var result = await reader.GetActualCostAsOfAsync(projectA, t);

        Assert.Equal(100_000.00m, result.Amount);
        Assert.Equal(1, result.EntryCount);
    }

    [Fact]
    public async Task GetActualCostAsOfAsync_Is_Driven_By_IncurredDate_Never_By_PostedAt_And_Nets_The_Accrual_Reversal_Correctly()
    {
        // Transcribes AC-1 + AC-2 (actual-cost.md §8) against the common-setup ledger: rows #1-4 are
        // ordinary March-and-earlier costs; #5/#6/#7 are the accrual/reversal/invoice triad, all
        // IncurredDate = 2026-03-31 but posted across April. Data date t1 = 2026-03-31 never moves;
        // what changes between the three "queried on" snapshots is which rows already exist in the
        // database, exactly mirroring the real-world sequence a job-cost ledger is posted in.
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var t1 = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var jan31 = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
        var feb28 = DateTimeOffset.Parse("2026-02-28T00:00:00+07:00");

        var entry1 = Entry(tenantId, projectId, 1_200_000.00m, jan31, DateTimeOffset.Parse("2026-02-05T00:00:00+07:00"));
        var entry2 = Entry(tenantId, projectId, 850_000.00m, feb28, DateTimeOffset.Parse("2026-03-04T00:00:00+07:00"));
        var entry3 = Entry(tenantId, projectId, 430_000.00m, feb28, DateTimeOffset.Parse("2026-03-04T00:00:00+07:00"));
        var entry4 = Entry(tenantId, projectId, 120_000.00m, t1, DateTimeOffset.Parse("2026-04-02T00:00:00+07:00"));

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ActualCostEntries.AddRange(entry1, entry2, entry3, entry4);
            await seedContext.SaveChangesAsync();
        }

        // --- "Queried on 2026-04-05": rows #1-4 exist. AC(t1) = 2,600,000.00 (AC-1). ---
        using (var readContext = factory.CreateContext())
        {
            var result = await new ActualCostReader(readContext).GetActualCostAsOfAsync(projectId, t1);
            Assert.Equal(2_600_000.00m, result.Amount);
            Assert.Equal(4, result.EntryCount);
        }

        // --- Accrual #5 lands (posted 2026-04-08). "Queried on 2026-04-10": AC(t1) = 3,200,000.00. ---
        var entry5Accrual = Entry(
            tenantId, projectId, 600_000.00m, t1, DateTimeOffset.Parse("2026-04-08T00:00:00+07:00"), ActualCostEntryType.Accrual);

        using (var writeContext = factory.CreateContext())
        {
            writeContext.ActualCostEntries.Add(entry5Accrual);
            await writeContext.SaveChangesAsync();
        }

        using (var readContext = factory.CreateContext())
        {
            var result = await new ActualCostReader(readContext).GetActualCostAsOfAsync(projectId, t1);
            Assert.Equal(3_200_000.00m, result.Amount);
            Assert.Equal(5, result.EntryCount);
        }

        // --- Reversal #6 + invoice #7 land (both posted 2026-04-20). "Queried on 2026-04-25":
        // AC(t1) = 3,200,000 - 600,000 + 640,000 = 3,240,000.00 (AC-2: the subcontract cost appears
        // once, at its true value, never double-counted). ---
        var entry6Reversal = Entry(
            tenantId, projectId, -600_000.00m, t1, DateTimeOffset.Parse("2026-04-20T00:00:00+07:00"),
            ActualCostEntryType.AccrualReversal, reversesEntryId: entry5Accrual.Id);
        var entry7Invoice = Entry(
            tenantId, projectId, 640_000.00m, t1, DateTimeOffset.Parse("2026-04-20T00:00:00+07:00"), ActualCostEntryType.Actual);

        using (var writeContext = factory.CreateContext())
        {
            writeContext.ActualCostEntries.AddRange(entry6Reversal, entry7Invoice);
            await writeContext.SaveChangesAsync();
        }

        using (var readContext = factory.CreateContext())
        {
            var result = await new ActualCostReader(readContext).GetActualCostAsOfAsync(projectId, t1);
            Assert.Equal(3_240_000.00m, result.Amount);
            Assert.Equal(7, result.EntryCount);
        }

        // --- AC-1's own explicit assertion: querying a LATER data date (2026-04-30) still returns
        // exactly 3,240,000.00 - entries #4-7 were posted in April but incurred in March, so no
        // April-incurred cost exists and the reversal does not leak forward. ---
        using (var readContext = factory.CreateContext())
        {
            var result = await new ActualCostReader(readContext)
                .GetActualCostAsOfAsync(projectId, DateTimeOffset.Parse("2026-04-30T00:00:00+07:00"));
            Assert.Equal(3_240_000.00m, result.Amount);
            Assert.Equal(7, result.EntryCount);
        }
    }
}
