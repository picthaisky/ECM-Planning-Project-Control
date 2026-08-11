using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Approval.Queries.SimulateRouting;

/// <summary>One rung of the simulated chain - the same shape as
/// <see cref="CMPlus.Application.Approval.ApprovalChainStep"/>, re-declared here (not reused
/// directly) purely so this feature's wire contract does not change if the internal routing-service
/// model ever does, mirroring how <c>ApprovalPolicyRuleDto</c> already sits alongside the Domain's
/// own <c>ApprovalPolicyRuleInput</c>.</summary>
public sealed record SimulatedApprovalChainStepDto(int StepNo, UserRole RequiredRole, int QuorumCount);

/// <summary>One of the two-or-more simultaneously-active policy rows ADR-0021 records as possible for
/// the <c>ProjectId IS NULL</c> (tenant-wide default) scope - present only when
/// <see cref="ApprovalRoutingSimulationDto.MultipleActivePoliciesDetected"/> is <see langword="true"/>.</summary>
public sealed record AmbiguousActivePolicyDto(Guid ApprovalPolicyId, int Version);

/// <summary>
/// The result of one routing simulation (S15-BE-01). <see cref="ApprovalPolicyId"/>/
/// <see cref="ApprovalPolicyVersion"/> disclose exactly which policy VERSION the chain was resolved
/// against - the same version a real Submit right now would pin onto the document - so an Admin
/// reading this response knows the answer corresponds to a specific, nameable policy edit, not merely
/// "whatever is active".
///
/// <para><b>ADR-0021.</b> <see cref="MultipleActivePoliciesDetected"/>/<see cref="AmbiguousActivePolicies"/>
/// surface the known single-active-index defect rather than hiding it: when more than one policy
/// row is simultaneously active for the exact scope <c>IApprovalRoutingService.Resolve</c> selected
/// from, the chain above is still exactly what a real Submit would produce right now (including
/// which of the tied versions happened to win) - see the handler's own remarks for why this is not
/// treated as a failure.</para>
/// </summary>
public sealed record ApprovalRoutingSimulationDto(
    ApprovalDocumentType DocumentType,
    Guid ProjectId,
    decimal InputAmount,
    decimal RoutingAmount,
    Guid ApprovalPolicyId,
    int ApprovalPolicyVersion,
    bool UsedFallbackChain,
    IReadOnlyList<SimulatedApprovalChainStepDto> Steps,
    bool EscalationApplied,
    bool AllowSelfApproval,
    bool MultipleActivePoliciesDetected,
    IReadOnlyList<AmbiguousActivePolicyDto> AmbiguousActivePolicies);
