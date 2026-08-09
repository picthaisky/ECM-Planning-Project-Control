using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>actual-cost.md §9/§10.1 (ADR-0013): <see cref="ActualCostEntry"/> is append-only -
/// verified structurally (no public mutating method, no public property setter), the same
/// discipline <c>ApprovalActionTests</c>/<c>EvmPeriodSnapshotTests</c> already established.</summary>
public class ActualCostEntryTests
{
    private static ActualCostEntry CreateEntry(
        decimal amount = 1_000_000.00m,
        Guid? wbsNodeId = null,
        Guid? activityId = null,
        Guid? reversesEntryId = null,
        string? note = null,
        decimal? quantity = null) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), wbsNodeId, activityId,
            CostCategory.Subcontract, ActualCostEntryType.Actual, ActualCostSource.ManualEntry,
            amount, DateTimeOffset.Parse("2026-01-31T00:00:00+07:00"), DateTimeOffset.Parse("2026-02-05T00:00:00+07:00"),
            Guid.NewGuid(), reversesEntryId, "INV-2026-0001", "5100-100", "ABC Subcontractor Co., Ltd.",
            note, fileImportJobId: null, paidDate: null, quantity, unitOfMeasure: quantity is null ? null : "m3");

    [Fact]
    public void Constructor_Assigns_All_Fields()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var wbsNodeId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var postedByUserId = Guid.NewGuid();
        var reversesEntryId = Guid.NewGuid();
        var fileImportJobId = Guid.NewGuid();
        var incurredDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var postedAt = DateTimeOffset.Parse("2026-04-02T00:00:00+07:00");
        var paidDate = DateTimeOffset.Parse("2026-05-01T00:00:00+07:00");

        var entry = new ActualCostEntry(
            tenantId, projectId, wbsNodeId, activityId,
            CostCategory.Material, ActualCostEntryType.Accrual, ActualCostSource.ManualEntry,
            850_000.00m, incurredDate, postedAt, postedByUserId, reversesEntryId,
            "PO-2026-042", "5100-200", "Siam Ready-Mix", "posted into a closed period", fileImportJobId,
            paidDate, quantity: 120.5000m, unitOfMeasure: "m3");

        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(projectId, entry.ProjectId);
        Assert.Equal(wbsNodeId, entry.WbsNodeId);
        Assert.Equal(activityId, entry.ActivityId);
        Assert.Equal(CostCategory.Material, entry.CostCategory);
        Assert.Equal(ActualCostEntryType.Accrual, entry.EntryType);
        Assert.Equal(ActualCostSource.ManualEntry, entry.Source);
        Assert.Equal(850_000.00m, entry.Amount);
        Assert.Equal(incurredDate, entry.IncurredDate);
        Assert.Equal(postedAt, entry.PostedAt);
        Assert.Equal(postedByUserId, entry.PostedByUserId);
        Assert.Equal(reversesEntryId, entry.ReversesEntryId);
        Assert.Equal("PO-2026-042", entry.DocumentReference);
        Assert.Equal("5100-200", entry.CostCode);
        Assert.Equal("Siam Ready-Mix", entry.VendorName);
        Assert.Equal("posted into a closed period", entry.Note);
        Assert.Equal(fileImportJobId, entry.FileImportJobId);
        Assert.Equal(paidDate, entry.PaidDate);
        Assert.Equal(120.5000m, entry.Quantity);
        Assert.Equal("m3", entry.UnitOfMeasure);
    }

    [Fact]
    public void Constructor_Allows_A_Negative_Amount_A_Reversal_Or_Credit_Note_Is_Never_Clamped()
    {
        // actual-cost.md §6.5/§7.6: AccrualReversal/Adjustment rows are legitimately negative -
        // AC = SUM(Amount) is a pure aggregate only because the sign is never coerced.
        var entry = CreateEntry(amount: -600_000.00m);

        Assert.Equal(-600_000.00m, entry.Amount);
    }

    [Fact]
    public void Constructor_Rejects_A_Zero_Amount()
    {
        // actual-cost.md §7.6: a zero cost entry, including a zero reversal, is noise.
        Assert.Throws<DomainException>(() => CreateEntry(amount: 0m));
    }

    [Theory]
    [InlineData(100.005)]
    [InlineData(100.001)]
    public void Constructor_Rejects_An_Amount_With_More_Than_Two_Decimal_Places(double amount)
    {
        // actual-cost.md §7.6: manual entry rejects > 2dp input outright (only the import path
        // rounds it and logs the adjustment instead).
        Assert.Throws<DomainException>(() => CreateEntry(amount: (decimal)amount));
    }

    [Fact]
    public void Constructor_Accepts_An_Amount_At_Exactly_Two_Decimal_Places()
    {
        var entry = CreateEntry(amount: 1_234.56m);
        Assert.Equal(1_234.56m, entry.Amount);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => new ActualCostEntry(
            Guid.NewGuid(), Guid.Empty, null, null,
            CostCategory.Other, ActualCostEntryType.Actual, ActualCostSource.ManualEntry,
            1_000.00m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), null,
            null, null, null, null, null, null, null, null));
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_PostedByUserId()
    {
        Assert.Throws<DomainException>(() => new ActualCostEntry(
            Guid.NewGuid(), Guid.NewGuid(), null, null,
            CostCategory.Other, ActualCostEntryType.Actual, ActualCostSource.ManualEntry,
            1_000.00m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.Empty, null,
            null, null, null, null, null, null, null, null));
    }

    [Fact]
    public void Constructor_Rejects_A_Negative_Quantity_When_Supplied()
    {
        Assert.Throws<DomainException>(() => CreateEntry(quantity: -1m));
    }

    [Fact]
    public void Constructor_Allows_Wbs_Node_And_Activity_And_Reverses_Entry_Id_To_Be_Null()
    {
        // NULL = project-level/unattributed (actual-cost.md §3) - the common case, not an error.
        var entry = CreateEntry(wbsNodeId: null, activityId: null, reversesEntryId: null);

        Assert.Null(entry.WbsNodeId);
        Assert.Null(entry.ActivityId);
        Assert.Null(entry.ReversesEntryId);
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(ActualCostEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        // IsSpecialName excludes property get_/set_ accessors and operator overloads; anything
        // left declared directly on ActualCostEntry itself (not inherited from Entity/object) would
        // be a mutator - there must be none (append-only, corrections are new rows).
        var mutatingMethods = typeof(ActualCostEntry)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(ActualCostEntry))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }

    [Fact]
    public void Sanity_Check_The_Fixture_Helper_Itself_Constructs_Successfully()
    {
        var entry = CreateEntry();
        Assert.NotEqual(Guid.Empty, entry.Id);
    }
}
