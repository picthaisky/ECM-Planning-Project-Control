using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S9-BE-04 DoD: <see cref="ProjectFinanceLedger"/> is append-only (no update/delete path)
/// - verified structurally (no public mutating method, no public property setter), the same
/// discipline <c>ApprovalActionTests</c>/<c>ActualCostEntryTests</c>/<c>EvmPeriodSnapshotTests</c>
/// already established. The factory methods below (<c>CreateRetentionAccrual</c> etc.) are
/// <b>not</b> mutators - each returns a brand-new instance.</summary>
public class ProjectFinanceLedgerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CertificateId = Guid.NewGuid();
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.Parse("2026-08-10T00:00:00+07:00");

    [Fact]
    public void Constructor_Assigns_All_Fields()
    {
        var entry = new ProjectFinanceLedger(
            TenantId, ProjectId, CertificateId, FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Accrual,
            1_080_000.00m, EffectiveDate, "IPC-1", "note");

        Assert.Equal(TenantId, entry.TenantId);
        Assert.Equal(ProjectId, entry.ProjectId);
        Assert.Equal(CertificateId, entry.PaymentCertificateId);
        Assert.Equal(FinanceLedgerCategory.Retention, entry.Category);
        Assert.Equal(FinanceLedgerEntryType.Accrual, entry.EntryType);
        Assert.Equal(1_080_000.00m, entry.Amount);
        Assert.Equal(EffectiveDate, entry.EffectiveDate);
        Assert.Equal("IPC-1", entry.Reference);
        Assert.Equal("note", entry.Note);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => new ProjectFinanceLedger(
            TenantId, Guid.Empty, CertificateId, FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Accrual,
            100m, EffectiveDate, null, null));
    }

    [Fact]
    public void Constructor_Rejects_A_Zero_Amount()
    {
        Assert.Throws<DomainException>(() => new ProjectFinanceLedger(
            TenantId, ProjectId, CertificateId, FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Accrual,
            0m, EffectiveDate, null, null));
    }

    [Theory]
    [InlineData(100.005)]
    [InlineData(100.001)]
    public void Constructor_Rejects_An_Amount_With_More_Than_Two_Decimal_Places(double amount)
    {
        Assert.Throws<DomainException>(() => new ProjectFinanceLedger(
            TenantId, ProjectId, CertificateId, FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Accrual,
            (decimal)amount, EffectiveDate, null, null));
    }

    [Theory]
    [InlineData(FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Disbursement)]
    [InlineData(FinanceLedgerCategory.Retention, FinanceLedgerEntryType.Recovery)]
    [InlineData(FinanceLedgerCategory.Advance, FinanceLedgerEntryType.Accrual)]
    [InlineData(FinanceLedgerCategory.Advance, FinanceLedgerEntryType.Release)]
    public void Constructor_Rejects_A_Category_EntryType_Combination_That_Makes_No_Domain_Sense(
        FinanceLedgerCategory category, FinanceLedgerEntryType entryType)
    {
        // The combination check runs before the PaymentCertificateId-required check, so a
        // certificate id is supplied here to isolate exactly the failure this test targets.
        Assert.Throws<DomainException>(() => new ProjectFinanceLedger(
            TenantId, ProjectId, CertificateId, category, entryType, 100m, EffectiveDate, null, null));
    }

    [Theory]
    [InlineData(FinanceLedgerEntryType.Accrual)]
    [InlineData(FinanceLedgerEntryType.Recovery)]
    public void Constructor_Requires_A_PaymentCertificateId_For_Accrual_And_Recovery(FinanceLedgerEntryType entryType)
    {
        var category = entryType == FinanceLedgerEntryType.Accrual ? FinanceLedgerCategory.Retention : FinanceLedgerCategory.Advance;

        Assert.Throws<DomainException>(() => new ProjectFinanceLedger(
            TenantId, ProjectId, paymentCertificateId: null, category, entryType, 100m, EffectiveDate, null, null));
    }

    [Fact]
    public void CreateRetentionAccrual_Requires_A_Positive_Amount()
    {
        var entry = ProjectFinanceLedger.CreateRetentionAccrual(TenantId, ProjectId, CertificateId, 1_080_000.00m, EffectiveDate);
        Assert.Equal(FinanceLedgerCategory.Retention, entry.Category);
        Assert.Equal(FinanceLedgerEntryType.Accrual, entry.EntryType);
        Assert.Equal(1_080_000.00m, entry.Amount);

        Assert.Throws<DomainException>(() =>
            ProjectFinanceLedger.CreateRetentionAccrual(TenantId, ProjectId, CertificateId, -1m, EffectiveDate));
        Assert.Throws<DomainException>(() =>
            ProjectFinanceLedger.CreateRetentionAccrual(TenantId, ProjectId, CertificateId, 0m, EffectiveDate));
    }

    [Fact]
    public void CreateRetentionRelease_Requires_A_Negative_Amount_And_No_Certificate()
    {
        var entry = ProjectFinanceLedger.CreateRetentionRelease(TenantId, ProjectId, -2_500_000.00m, EffectiveDate, "Substantial completion");
        Assert.Equal(FinanceLedgerCategory.Retention, entry.Category);
        Assert.Equal(FinanceLedgerEntryType.Release, entry.EntryType);
        Assert.Equal(-2_500_000.00m, entry.Amount);
        Assert.Null(entry.PaymentCertificateId);

        Assert.Throws<DomainException>(() =>
            ProjectFinanceLedger.CreateRetentionRelease(TenantId, ProjectId, 2_500_000.00m, EffectiveDate, "wrong sign"));
    }

    [Fact]
    public void CreateAdvanceRecovery_Requires_A_Positive_Amount()
    {
        var entry = ProjectFinanceLedger.CreateAdvanceRecovery(TenantId, ProjectId, CertificateId, 2_160_000.00m, EffectiveDate);
        Assert.Equal(FinanceLedgerCategory.Advance, entry.Category);
        Assert.Equal(FinanceLedgerEntryType.Recovery, entry.EntryType);

        Assert.Throws<DomainException>(() =>
            ProjectFinanceLedger.CreateAdvanceRecovery(TenantId, ProjectId, CertificateId, -1m, EffectiveDate));
    }

    [Fact]
    public void CreateAdjustment_Requires_A_Non_Blank_Note_But_Allows_Either_Sign()
    {
        var positive = ProjectFinanceLedger.CreateAdjustment(
            TenantId, ProjectId, null, FinanceLedgerCategory.Retention, 50_000.00m, EffectiveDate, "REF-1", "correcting an under-accrual");
        Assert.Equal(50_000.00m, positive.Amount);

        var negative = ProjectFinanceLedger.CreateAdjustment(
            TenantId, ProjectId, null, FinanceLedgerCategory.Advance, -50_000.00m, EffectiveDate, "REF-2", "correcting an over-recovery");
        Assert.Equal(-50_000.00m, negative.Amount);

        Assert.Throws<DomainException>(() => ProjectFinanceLedger.CreateAdjustment(
            TenantId, ProjectId, null, FinanceLedgerCategory.Retention, 50_000.00m, EffectiveDate, "REF-3", note: "  "));
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(ProjectFinanceLedger)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        // IsSpecialName excludes property get_/set_ accessors and operator overloads. The static
        // Create* factories are declared on the type but are not *instance* methods, so they are
        // correctly excluded here too - append-only means no instance mutator, which these are not.
        var mutatingMethods = typeof(ProjectFinanceLedger)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(ProjectFinanceLedger))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }
}
