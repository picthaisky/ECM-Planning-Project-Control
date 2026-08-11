using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.SimulateRouting;

/// <summary>
/// S15-BE-01: "ป้อนจำนวนเงินสมมติแล้วได้ chain ที่จะเกิดขึ้นจริงโดยไม่สร้างเอกสาร" - lets a tenant Admin
/// feed a hypothetical amount and see the EXACT chain a real Submit would resolve right now, without
/// creating any document.
///
/// <para><b>Reuse, not re-derivation.</b> This handler calls the identical two collaborators
/// <see cref="CMPlus.Application.Features.VariationOrder.Commands.Submit.SubmitVariationOrderCommandHandler"/>/
/// <see cref="CMPlus.Application.Features.Payment.Commands.Submit.SubmitPaymentCertificateCommandHandler"/>
/// call for the routing decision itself - <see cref="IApprovalPolicyReader.GetCandidatePoliciesAsync"/>
/// then <see cref="IApprovalRoutingService.Resolve"/> - and, for a Variation Order, fetches the two
/// escalation inputs (<see cref="IProjectRepository.FindAsync"/>'s
/// <c>Project.EscalationBaselineContractValue</c> and
/// <see cref="IVariationOrderRepository.GetApprovedNetSignedTotalAsync"/>) the exact same way
/// <c>SubmitVariationOrderCommandHandler</c> does. Nothing here re-implements the band/escalation
/// algorithm a second time. A simulator that computed the chain a different way could agree with the
/// real path in tests and disagree in production - this codebase already shipped that exact defect
/// class once (H-01: a re-derivation that dropped the amount-band filter let a QS certify a
/// five-million-baht certificate) - so reusing the one true resolution path is the whole point of
/// this feature, not an implementation detail.</para>
///
/// <para><b>ADR-0021 (two simultaneously-active tenant-wide policies).</b> The filtered unique index
/// backing "one active policy per scope" is proven broken for the <c>ProjectId IS NULL</c> group
/// (tenant-wide default), so <see cref="IApprovalPolicyReader.GetCandidatePoliciesAsync"/> can
/// legitimately return more than one row for the exact scope <see cref="IApprovalRoutingService.Resolve"/>
/// would select from. <c>Resolve</c> itself has no opinion about this - it silently
/// <c>FirstOrDefault</c>s, the same as a real Submit would - so the chain this handler returns is
/// exactly the chain a real Submit would produce right now, INCLUDING its nondeterminism. This
/// handler deliberately does NOT fail closed and does NOT silently pick a "canonical" one of the
/// duplicates - either would make the simulator disagree with the real resolution path it exists to
/// mirror. Instead it separately detects the corruption (more than one candidate policy present in
/// the scope that was actually selected) and surfaces it via
/// <see cref="ApprovalRoutingSimulationDto.MultipleActivePoliciesDetected"/>/
/// <see cref="ApprovalRoutingSimulationDto.AmbiguousActivePolicies"/> - silently picking one would
/// hide the corruption; surfacing it turns the simulator into a detection tool for exactly the defect
/// ADR-0021 records, on top of being a routing preview.</para>
///
/// <para><b>Zero side effects by construction.</b> Every collaborator here is a reader
/// (<see cref="IApprovalPolicyReader"/>, <see cref="IProjectRepository.FindAsync"/> - never followed
/// by <c>TrySaveChangesAsync</c> - and <see cref="IVariationOrderRepository.GetApprovedNetSignedTotalAsync"/>,
/// a pure aggregate query) or the pure, EF-free <see cref="IApprovalRoutingService"/>. This handler
/// never calls any repository's <c>SaveChangesAsync</c>/<c>TrySaveChangesAsync</c>, stages no entity
/// on any change tracker, and depends on neither <see cref="IApprovalActionRepository"/> nor any
/// audit-writing collaborator - so it can create no document, no
/// <see cref="CMPlus.Domain.Entities.ApprovalAction"/> and no
/// <see cref="CMPlus.Domain.Entities.AuditLog"/> row. qa-engineer should assert this by unchanged row
/// counts against a real <c>DbContext</c>, not merely that the HTTP call returned 200.</para>
/// </summary>
public sealed class SimulateApprovalRoutingQueryHandler(
    IApprovalPolicyReader policyReader,
    IApprovalRoutingService routingService,
    IProjectRepository projectRepository,
    IVariationOrderRepository variationOrderRepository,
    IDateTimeProvider clock)
    : IRequestHandler<SimulateApprovalRoutingQuery, Result<ApprovalRoutingSimulationDto>>
{
    public async Task<Result<ApprovalRoutingSimulationDto>> Handle(
        SimulateApprovalRoutingQuery request, CancellationToken cancellationToken)
    {
        // Tenant-scoped by the ambient global query filter (ADR-0002) - a ProjectId belonging to
        // another tenant is indistinguishable from "does not exist", exactly like every other
        // IProjectRepository.FindAsync caller. Checked explicitly (rather than only implicitly via
        // whatever GetCandidatePoliciesAsync returns) because ApprovalPolicy lookups do not
        // themselves depend on ProjectId being real - a route-trusting implementation could
        // otherwise "successfully" simulate against a project id from another tenant.
        var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<ApprovalRoutingSimulationDto>.Failure(ApprovalSimulationErrorCodes.ProjectNotFound);
        }

        var now = clock.UtcNow;

        // The exact set IApprovalRoutingService.Resolve selects from for a real Submit right now -
        // see that method's own remarks on why GetCandidatePoliciesAsync, not GetByIdAsync, is the
        // correct call here (this is an as-yet-unrouted hypothetical, not a pinned document).
        var candidatePolicies = await policyReader.GetCandidatePoliciesAsync(request.DocumentType, now, cancellationToken);

        decimal? escalationBaseline = null;
        decimal? cumulativeApprovedVo = null;
        if (request.DocumentType == ApprovalDocumentType.VariationOrder)
        {
            // Same two values SubmitVariationOrderCommandHandler feeds Resolve for a real
            // submission, fetched the same way from the same collaborators - so the escalation test
            // the simulator runs is identical, not merely similar, to what a real submission of this
            // project would run.
            escalationBaseline = project.EscalationBaselineContractValue;
            cumulativeApprovedVo = await variationOrderRepository.GetApprovedNetSignedTotalAsync(request.ProjectId, cancellationToken);
        }

        var routingResult = routingService.Resolve(new ApprovalRoutingRequest(
            request.DocumentType,
            request.ProjectId,
            request.Amount,
            now,
            candidatePolicies,
            EscalationBaselineContractValue: escalationBaseline,
            CumulativeApprovedVoAmount: cumulativeApprovedVo));

        if (routingResult.IsFailure)
        {
            // ApprovalErrorCodes.PolicyGap / ContractValueNotConfigured - the same failure a real
            // Submit would hit right now with this amount. Surfaced as-is, never swallowed: "this
            // submission would be blocked" IS the honest simulation result, not an error in the
            // simulator itself.
            return Result<ApprovalRoutingSimulationDto>.Failure(routingResult.Error);
        }

        var chain = routingResult.Value;

        // ADR-0021 detection: which ProjectId scope did Resolve() actually select from? Mirrors
        // ApprovalRoutingService.SelectPolicy's own precedence (project override over tenant
        // default) without re-implementing its band/escalation logic - this only needs to know which
        // group was live, not what chain it produces (Resolve already computed that above).
        var selectedScopeProjectId = candidatePolicies.Any(p => p.ProjectId == request.ProjectId)
            ? request.ProjectId
            : (Guid?)null;

        var candidatesInSelectedScope = candidatePolicies.Where(p => p.ProjectId == selectedScopeProjectId).ToList();
        var multiplePoliciesDetected = candidatesInSelectedScope.Count > 1;

        var dto = new ApprovalRoutingSimulationDto(
            request.DocumentType,
            request.ProjectId,
            request.Amount,
            chain.RoutingAmount,
            chain.ApprovalPolicyId,
            chain.ApprovalPolicyVersion,
            UsedFallbackChain: chain.ApprovalPolicyId == Guid.Empty,
            chain.Steps.Select(s => new SimulatedApprovalChainStepDto(s.StepNo, s.RequiredRole, s.QuorumCount)).ToList(),
            chain.EscalationApplied,
            chain.AllowSelfApproval,
            multiplePoliciesDetected,
            multiplePoliciesDetected
                ? candidatesInSelectedScope.Select(p => new AmbiguousActivePolicyDto(p.Id, p.Version)).ToList()
                : []);

        return Result<ApprovalRoutingSimulationDto>.Success(dto);
    }
}
