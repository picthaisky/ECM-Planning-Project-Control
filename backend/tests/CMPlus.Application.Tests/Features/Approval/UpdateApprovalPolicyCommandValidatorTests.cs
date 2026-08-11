using CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Approval;

/// <summary>
/// Covers the <c>QuorumCount</c> upper bound added for S9-SEC-02 finding N-02.
///
/// <para>Why this bound matters, since a bare range check looks like boilerplate: before H-02 was
/// fixed, <c>QuorumCount</c> was accepted and stored but never read, so an absurd value was inert.
/// H-02's fix made the engine genuinely enforce it — which also made an over-large value
/// <em>binding</em>. `DuplicateChainVoter` (ADR-0016; was `DuplicateChainApprover`) caps the
/// achievable quorum at the number of distinct users holding the required role, so a quorum
/// larger than that can never be satisfied: the
/// certificate sits in <c>PendingApproval</c> permanently with its money fields frozen, and there
/// is currently no withdraw/cancel endpoint to recover it. Fixing one finding created the
/// conditions for another, which is exactly the kind of thing that needs a regression test rather
/// than a comment.</para>
/// </summary>
public class UpdateApprovalPolicyCommandValidatorTests
{
    private static readonly UpdateApprovalPolicyCommandValidator Validator = new();

    private static UpdateApprovalPolicyCommand CommandWithQuorum(int quorumCount) =>
        new(
            ApprovalDocumentType.PaymentCertificate,
            AllowSelfApproval: false,
            CumulativeVoEscalationPct: null,
            CumulativeVoEscalationRole: null,
            Rules:
            [
                new ApprovalPolicyRuleInput(
                    StepNo: 1,
                    MinAmount: 0m,
                    MaxAmount: null,
                    RequiredRole: UserRole.QS,
                    RequiredUserId: null,
                    QuorumCount: quorumCount),
            ]);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(UpdateApprovalPolicyCommandValidator.MaxQuorumCount)]
    public void A_Quorum_Within_The_Practical_Range_Is_Accepted(int quorumCount)
    {
        var result = Validator.Validate(CommandWithQuorum(quorumCount));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Quorum_Below_One_Is_Rejected(int quorumCount)
    {
        var result = Validator.Validate(CommandWithQuorum(quorumCount));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains(nameof(ApprovalPolicyRuleInput.QuorumCount)));
    }

    [Theory]
    [InlineData(UpdateApprovalPolicyCommandValidator.MaxQuorumCount + 1)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void A_Quorum_Above_The_Practical_Ceiling_Is_Rejected_Rather_Than_Stranding_The_Document(int quorumCount)
    {
        var result = Validator.Validate(CommandWithQuorum(quorumCount));

        Assert.False(result.IsValid);
        var error = Assert.Single(
            result.Errors,
            e => e.PropertyName.Contains(nameof(ApprovalPolicyRuleInput.QuorumCount)));

        // The message must explain the consequence, not just state a range - an Admin who hits this
        // needs to understand *why* 9 approvers is not merely disallowed but unsatisfiable.
        Assert.Contains("never be satisfied", error.ErrorMessage);
    }
}
