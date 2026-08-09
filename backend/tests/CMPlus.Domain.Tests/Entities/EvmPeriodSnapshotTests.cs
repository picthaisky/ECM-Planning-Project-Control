using System.Reflection;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S7-BE-05 DoD: <see cref="EvmPeriodSnapshot"/> is immutable - "no update path at all" -
/// verified structurally (no public mutating method, no public property setter), the same discipline
/// <c>ApprovalActionTests</c> already established for <see cref="CMPlus.Domain.Entities.ApprovalAction"/>.</summary>
public class EvmPeriodSnapshotTests
{
    private static EvmPeriodSnapshot CreateComputableSnapshot() => new(
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
        bac: 1_000_000.00m, pv: 400_000.00m, ev: 300_000.00m, ac: 350_000.00m,
        eacVariant: EacVariant.CpiBased, performanceFactor: 1.166667m, eac: 1_166_666.67m,
        etc: 816_666.67m, vac: -166_666.67m, createdAt: DateTimeOffset.UtcNow, createdByUserId: Guid.NewGuid());

    [Fact]
    public void Constructor_Assigns_And_Rounds_Every_Field()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var dataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");
        var userId = Guid.NewGuid();

        // Deliberately supplied at more precision than the entity's own columns carry, to prove the
        // constructor itself applies RoundingRules (belt-and-braces, mirroring Project's own
        // MoneyGuard/PercentageGuard re-application) rather than trusting an already-rounded caller.
        var snapshot = new EvmPeriodSnapshot(
            tenantId, projectId, dataDate,
            bac: 1_000_000.005m, pv: 400_000.004m, ev: 300_000.006m, ac: 350_000.001m,
            eacVariant: EacVariant.CpiBased, performanceFactor: 1.16666666666666m,
            eac: 1_166_666.666666m, etc: 816_666.666666m, vac: -166_666.666666m,
            createdAt: DateTimeOffset.Parse("2026-07-01T00:00:00Z"), createdByUserId: userId);

        Assert.Equal(tenantId, snapshot.TenantId);
        Assert.Equal(projectId, snapshot.ProjectId);
        Assert.Equal(dataDate, snapshot.DataDate);
        Assert.Equal(1_000_000.01m, snapshot.Bac); // .005 away-from-zero -> .01, not banker's .00.
        Assert.Equal(400_000.00m, snapshot.Pv);
        Assert.Equal(300_000.01m, snapshot.Ev);
        Assert.Equal(350_000.00m, snapshot.Ac);
        Assert.Equal(EacVariant.CpiBased, snapshot.EacVariant);
        Assert.Equal(1.166667m, snapshot.PerformanceFactor);
        Assert.Equal(1_166_666.67m, snapshot.Eac);
        Assert.Equal(816_666.67m, snapshot.Etc);
        Assert.Equal(-166_666.67m, snapshot.Vac);
        Assert.Equal(userId, snapshot.CreatedByUserId);
    }

    [Fact]
    public void Constructor_Accepts_Null_Eac_Etc_Vac_Performance_Factor_For_A_NonComputable_Variant()
    {
        // Freezing a period where the project's default variant was itself not computable at that
        // data date (e.g. a not-started project) - the snapshot must be able to represent that
        // honestly, never substituting a fabricated 0.00.
        var snapshot = new EvmPeriodSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            bac: 0m, pv: 0m, ev: 0m, ac: 0m,
            eacVariant: EacVariant.CpiBased, performanceFactor: null, eac: null, etc: null, vac: null,
            createdAt: DateTimeOffset.UtcNow, createdByUserId: null);

        Assert.Null(snapshot.PerformanceFactor);
        Assert.Null(snapshot.Eac);
        Assert.Null(snapshot.Etc);
        Assert.Null(snapshot.Vac);
        Assert.Null(snapshot.CreatedByUserId);
    }

    [Fact]
    public void Constructor_Allows_A_Negative_Etc_Eac_Vac_They_Are_Not_Guarded_Non_Negative()
    {
        var snapshot = new EvmPeriodSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            bac: 1_000_000m, pv: 800_000m, ev: 1_200_000m, ac: 900_000m,
            eacVariant: EacVariant.CpiBased, performanceFactor: 0.75m, eac: 750_000m, etc: -150_000m,
            vac: 250_000m, createdAt: DateTimeOffset.UtcNow, createdByUserId: null);

        Assert.Equal(-150_000m, snapshot.Etc);
    }

    [Fact]
    public void Constructor_Allows_A_Negative_Ac_It_Degrades_Gracefully_Instead_Of_Throwing()
    {
        // Regression test for the latent defect fixed alongside ActualCostEntry (found by
        // qa-engineer in Sprint 7 review, recorded in ADR-0013's consequences): closing a period
        // used to throw DomainException the moment AC(t) went negative (a credit note/over-reversal
        // exceeding recorded costs, actual-cost.md §6.5/§7.6 - "compute and return as-is; do not
        // clamp"). That path was unreachable while AC was pinned at a literal 0, but is real once a
        // genuine ActualCostEntry ledger exists.
        var snapshot = new EvmPeriodSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            bac: 1_000_000m, pv: 400_000m, ev: 300_000m, ac: -50_000m,
            eacVariant: EacVariant.CpiBased, performanceFactor: null, eac: null, etc: null,
            vac: null, createdAt: DateTimeOffset.UtcNow, createdByUserId: null);

        Assert.Equal(-50_000m, snapshot.Ac);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<CMPlus.Domain.Common.DomainException>(() => new EvmPeriodSnapshot(
            Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow,
            bac: 0m, pv: 0m, ev: 0m, ac: 0m,
            eacVariant: EacVariant.CpiBased, performanceFactor: null, eac: null, etc: null, vac: null,
            createdAt: DateTimeOffset.UtcNow, createdByUserId: null));
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(EvmPeriodSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        // IsSpecialName excludes property get_/set_ accessors and operator overloads; anything left
        // declared directly on EvmPeriodSnapshot itself (not inherited from Entity/object) would be a
        // mutator - there must be none (immutable, S7-BE-05 DoD: "no update path at all").
        var mutatingMethods = typeof(EvmPeriodSnapshot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(EvmPeriodSnapshot))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }

    [Fact]
    public void Sanity_Check_The_Fixture_Helper_Itself_Constructs_Successfully()
    {
        var snapshot = CreateComputableSnapshot();
        Assert.NotEqual(Guid.Empty, snapshot.Id);
    }
}
