using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class ProjectTests
{
    private static Project CreateProject(decimal? retentionRate = null, decimal? advanceRate = null) =>
        Project.Create(
            tenantId: Guid.NewGuid(),
            name: "Test Project",
            code: "P-001",
            owner: "Owner Co.",
            contractStart: DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"),
            contractFinish: DateTimeOffset.Parse("2026-12-31T00:00:00+07:00"),
            bac: 1_000_000m,
            dataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
            retentionRate: retentionRate,
            advanceRate: advanceRate);

    [Fact]
    public void RetentionRate_Null_Is_Distinguishable_From_Zero()
    {
        var withNull = CreateProject(retentionRate: null);
        var withZero = CreateProject(retentionRate: 0m);

        Assert.Null(withNull.RetentionRate);
        Assert.NotNull(withZero.RetentionRate);
        Assert.Equal(0m, withZero.RetentionRate);
    }

    [Fact]
    public void AdvanceRate_Null_Is_Distinguishable_From_Zero()
    {
        var withNull = CreateProject(advanceRate: null);
        var withZero = CreateProject(advanceRate: 0m);

        Assert.Null(withNull.AdvanceRate);
        Assert.NotNull(withZero.AdvanceRate);
        Assert.Equal(0m, withZero.AdvanceRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(42.5, 42.5)]
    public void RetentionRate_Clamps_To_0_100(decimal input, decimal expected)
    {
        var project = CreateProject(retentionRate: input);

        Assert.Equal(expected, project.RetentionRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(42.5, 42.5)]
    public void AdvanceRate_Clamps_To_0_100(decimal input, decimal expected)
    {
        // Parity gap: RetentionRate had this theory, AdvanceRate (same PercentageGuard.Clamp
        // setter shape, Project.cs) did not - S4-QA-02 two-layer verification (US-4.4) needs both
        // percent fields checked at the domain defense-in-depth layer, not just one.
        var project = CreateProject(advanceRate: input);

        Assert.Equal(expected, project.AdvanceRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void SetRetentionRate_Clamps_To_0_100_At_The_Setter(decimal input, decimal expected)
    {
        // S4-BE-02's UpdateProjectCommandHandler calls Project.SetRetentionRate (not the
        // constructor) on every edit - confirm the setter clamps identically to the constructor
        // path exercised above, since these are two different code paths in Project.cs.
        var project = CreateProject();

        project.SetRetentionRate(input);

        Assert.Equal(expected, project.RetentionRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void SetAdvanceRate_Clamps_To_0_100_At_The_Setter(decimal input, decimal expected)
    {
        var project = CreateProject();

        project.SetAdvanceRate(input);

        Assert.Equal(expected, project.AdvanceRate);
    }

    [Fact]
    public void SetRetentionCapPercentage_Clamps_At_Setter()
    {
        var project = CreateProject();

        project.SetRetentionCapPercentage(250m);

        Assert.Equal(100m, project.RetentionCapPercentage);
    }

    [Fact]
    public void SetRetentionCapPercentage_Null_Means_Uncapped()
    {
        var project = CreateProject();

        project.SetRetentionCapPercentage(null);

        Assert.Null(project.RetentionCapPercentage);
    }

    [Fact]
    public void ContractValue_Defaults_To_Bac_When_Not_Specified()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow);

        Assert.Equal(500_000m, project.ContractValue);
    }

    [Fact]
    public void ContractValue_Can_Be_Overridden()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow, contractValue: 480_000m);

        Assert.Equal(480_000m, project.ContractValue);
    }

    [Fact]
    public void EacVariantDefault_Defaults_To_CpiBased()
    {
        var project = CreateProject();

        Assert.Equal(EacVariant.CpiBased, project.EacVariantDefault);
    }

    [Fact]
    public void SetEacVariantDefault_Changes_The_Variant()
    {
        var project = CreateProject();

        project.SetEacVariantDefault(EacVariant.CpiSpiBased);

        Assert.Equal(EacVariant.CpiSpiBased, project.EacVariantDefault);
    }

    [Fact]
    public void SetEacCustomPerformanceFactor_Rejects_Zero_Or_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetEacCustomPerformanceFactor(0m));
        Assert.Throws<DomainException>(() => project.SetEacCustomPerformanceFactor(-1.5m));
    }

    [Fact]
    public void SetEacCustomPerformanceFactor_Accepts_Positive_Value()
    {
        var project = CreateProject();

        project.SetEacCustomPerformanceFactor(1.25m);

        Assert.Equal(1.25m, project.EacCustomPerformanceFactor);
    }

    [Fact]
    public void SetEacManualEtc_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetEacManualEtc(-1m));
    }

    [Fact]
    public void SetEacManualEtc_Accepts_Zero_Or_Positive()
    {
        var project = CreateProject();

        project.SetEacManualEtc(0m);
        Assert.Equal(0m, project.EacManualEtc);

        project.SetEacManualEtc(760_000.00m);
        Assert.Equal(760_000.00m, project.EacManualEtc);
    }

    [Fact]
    public void Constructor_Rejects_Negative_Bac()
    {
        Assert.Throws<DomainException>(() => Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
            bac: -1m, dataDate: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetDefectsLiabilityMonths_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetDefectsLiabilityMonths(-1));
    }

    [Fact]
    public void SetAdvanceAmountPaid_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetAdvanceAmountPaid(-100m));
    }

    [Fact]
    public void RetentionRelease1Percentage_Defaults_To_50()
    {
        var project = CreateProject();

        Assert.Equal(50.00m, project.RetentionRelease1Percentage);
    }

    [Fact]
    public void AdvanceRecoveryMethod_Defaults_To_ProRata()
    {
        var project = CreateProject();

        Assert.Equal(AdvanceRecoveryMethod.ProRata, project.AdvanceRecoveryMethod);
    }

    [Fact]
    public void SetAdvanceRecoveryMethod_ThresholdBanded_Stores_Banded_Params_Clamped()
    {
        var project = CreateProject();

        project.SetAdvanceRecoveryMethod(AdvanceRecoveryMethod.ThresholdBanded, startPct: 10m, ratePct: 125m, endPct: 90m);

        Assert.Equal(AdvanceRecoveryMethod.ThresholdBanded, project.AdvanceRecoveryMethod);
        Assert.Equal(10m, project.AdvanceRecoveryStartPct);
        Assert.Equal(100m, project.AdvanceRecoveryRatePct); // clamped from 125
        Assert.Equal(90m, project.AdvanceRecoveryEndPct);
    }

    [Fact]
    public void Constructor_Rejects_Blank_Required_Fields()
    {
        Assert.Throws<DomainException>(() => Project.Create(
            Guid.NewGuid(), "  ", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
            bac: 1m, dataDate: DateTimeOffset.UtcNow));
    }

    // ------------------------------------------------------------------------------------
    // S4-BE-02 (US-4.3/4.4): UpdateProjectCommand's domain-level defense-in-depth setters.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void SetContractDates_Rejects_Finish_Before_Start()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetContractDates(
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), DateTimeOffset.Parse("2026-05-01T00:00:00Z")));
    }

    [Fact]
    public void SetContractDates_Accepts_Finish_Equal_To_Start()
    {
        var project = CreateProject();
        var same = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

        project.SetContractDates(same, same);

        Assert.Equal(same, project.ContractStart);
        Assert.Equal(same, project.ContractFinish);
    }

    [Fact]
    public void SetContractDates_Accepts_Finish_After_Start()
    {
        var project = CreateProject();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var finish = DateTimeOffset.Parse("2026-12-31T00:00:00Z");

        project.SetContractDates(start, finish);

        Assert.Equal(start, project.ContractStart);
        Assert.Equal(finish, project.ContractFinish);
    }

    [Fact]
    public void SetCode_Rejects_Blank()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetCode("  "));
    }

    [Fact]
    public void SetCode_Trims_And_Assigns()
    {
        var project = CreateProject();

        project.SetCode(" P-002 ");

        Assert.Equal("P-002", project.Code);
    }

    [Fact]
    public void SetOwner_Rejects_Blank()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetOwner(""));
    }

    [Fact]
    public void SetOwner_Trims_And_Assigns()
    {
        var project = CreateProject();

        project.SetOwner(" New Owner Co. ");

        Assert.Equal("New Owner Co.", project.Owner);
    }

    [Fact]
    public void SetBac_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetBac(-1m));
    }

    [Fact]
    public void SetBac_Accepts_Zero_Or_Positive()
    {
        var project = CreateProject();

        project.SetBac(2_500_000.50m);

        Assert.Equal(2_500_000.50m, project.BAC);
    }
}
