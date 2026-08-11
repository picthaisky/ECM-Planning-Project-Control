using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S2-BE-04: rule-band validation (non-overlapping, gap-free per StepNo) and
/// version-pinning (edit creates Version+1, never mutates in place).</summary>
public class ApprovalPolicyTests
{
    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> ThDefaultVoRules =
    [
        new(1, 0.00m, 500_000.00m, UserRole.PM),
        new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
        new(2, 500_000.00m, 5_000_000.00m, UserRole.ProjectDirector),
        new(1, 5_000_000.00m, null, UserRole.PM),
        new(2, 5_000_000.00m, null, UserRole.ProjectDirector),
        new(3, 5_000_000.00m, null, UserRole.Executive),
    ];

    [Fact]
    public void CreateInitialVersion_Accepts_The_TH_Default_VO_Band_Shape()
    {
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, ThDefaultVoRules,
            cumulativeVoEscalationPct: 10.00m, cumulativeVoEscalationRole: UserRole.Executive);

        Assert.Equal(1, policy.Version);
        Assert.True(policy.IsActive);
        Assert.Equal(6, policy.Rules.Count);
    }

    [Fact]
    public void CreateInitialVersion_Accepts_A_Sparse_Policy_That_Does_Not_Start_At_Zero()
    {
        // approval-workflow.md §8 fixture R5: a policy whose lowest MinAmount is 100,000.00 is a
        // legitimate (if unusual) configuration - it fails closed at *routing* time for amounts
        // below that, not at save time.
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow,
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.PM)]);

        Assert.Single(policy.Rules);
    }

    [Fact]
    public void CreateInitialVersion_Rejects_A_Missing_StepNo_In_The_Middle_Of_A_Band()
    {
        // StepNo 2 is entirely absent for [500000,5000000) even though StepNo 1 and 3 both cover it.
        var brokenRules = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 500_000.00m, UserRole.PM),
            new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
            new(3, 500_000.00m, 5_000_000.00m, UserRole.Executive),
        };

        var ex = Assert.Throws<DomainException>(() => ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, brokenRules));

        // [500000,5000000) resolves to steps {1,3} - missing StepNo 2, the specific offending step.
        Assert.Contains("StepNo 2", ex.Message);
    }

    [Fact]
    public void CreateInitialVersion_Rejects_Overlapping_Bands_For_The_Same_StepNo()
    {
        // Two different StepNo-1 rows both covering 400,000 (overlap: [0,500000) and [300000,600000)).
        var overlapping = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 500_000.00m, UserRole.PM),
            new(2, 300_000.00m, 600_000.00m, UserRole.ProjectDirector),
        };

        Assert.Throws<DomainException>(() => ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, overlapping));
    }

    [Fact]
    public void CreateInitialVersion_Rejects_Two_Rules_That_Genuinely_Share_A_StepNo_Over_An_Overlapping_Amount_Range()
    {
        // security review sprint-09.md M-03: unlike CreateInitialVersion_Rejects_Overlapping_Bands_For_The_Same_StepNo
        // above (which despite its name uses two DIFFERENT StepNos), this is the actual M-03 shape -
        // two DIFFERENT rules sharing the SAME StepNo, both covering the amount 400,000.00. Routing an
        // amount in [300000,500000) against this policy would previously resolve BOTH rules (since
        // AssertContiguousStepsAt's .Distinct() silently collapsed them to one StepNo for the
        // save-time check), producing a chain with two entries at StepNo 1 - exactly the "resolved
        // steps: [1,1]" data corruption the review proved with ApprovalPolicySeeder-style direct
        // construction (probe 8). Constructed directly via CreateInitialVersion (not through
        // UpdateApprovalPolicyCommandHandler's own additive FindOverlappingStepNo pre-check) so this
        // proves the invariant lives on the aggregate itself and protects every construction path,
        // including ApprovalPolicySeeder, which calls CreateInitialVersion directly.
        var overlappingSameStep = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 500_000.00m, UserRole.PM),
            new(1, 300_000.00m, 600_000.00m, UserRole.QS),
        };

        var ex = Assert.Throws<DomainException>(() => ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, overlappingSameStep));

        Assert.Contains("StepNo 1", ex.Message);
    }

    [Fact]
    public void CreateInitialVersion_Rejects_Empty_Rule_List()
    {
        Assert.Throws<DomainException>(() => ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, []));
    }

    [Fact]
    public void CreateNextVersion_Produces_A_New_Instance_With_Version_Plus_One_Never_Mutating_The_Original()
    {
        var v1 = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, ThDefaultVoRules);

        var v2 = v1.CreateNextVersion(
            DateTimeOffset.UtcNow, ThDefaultVoRules, allowSelfApproval: true,
            cumulativeVoEscalationPct: 15.00m, cumulativeVoEscalationRole: UserRole.Executive);

        Assert.Equal(1, v1.Version);
        Assert.True(v1.IsActive);
        Assert.Equal(2, v2.Version);
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.True(v2.IsActive);
        Assert.Equal(15.00m, v2.CumulativeVoEscalationPct);
    }

    [Fact]
    public void Deactivate_Sets_IsActive_False_And_Stamps_EffectiveTo()
    {
        var policy = ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, ThDefaultVoRules);

        var effectiveTo = DateTimeOffset.UtcNow.AddDays(1);
        policy.Deactivate(effectiveTo);

        Assert.False(policy.IsActive);
        Assert.Equal(effectiveTo, policy.EffectiveTo);
    }

    [Fact]
    public void CreateInitialVersion_Rejects_Escalation_Percentage_Outside_0_100()
    {
        Assert.Throws<DomainException>(() => ApprovalPolicy.CreateInitialVersion(
            Guid.NewGuid(), null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow, ThDefaultVoRules,
            cumulativeVoEscalationPct: 150m));
    }
}
