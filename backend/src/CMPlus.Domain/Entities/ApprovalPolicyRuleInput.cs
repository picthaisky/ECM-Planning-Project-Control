using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Plain data shape for one rule row supplied to <see cref="ApprovalPolicy.CreateInitialVersion"/>/
/// <see cref="ApprovalPolicy.CreateNextVersion"/>, before it becomes an <see cref="ApprovalPolicyRule"/>
/// entity (which needs the owning policy's <c>Id</c> to construct, not yet known to the caller).
/// </summary>
public sealed record ApprovalPolicyRuleInput(
    int StepNo,
    decimal MinAmount,
    decimal? MaxAmount,
    UserRole RequiredRole,
    Guid? RequiredUserId = null,
    int QuorumCount = 1);
