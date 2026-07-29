using CMPlus.Domain.Common;

namespace CMPlus.Application.Approval;

/// <summary>
/// The pure, fail-closed amount-tiered approval routing algorithm (ADR-0008, approval-workflow.md
/// §5.3). Deliberately takes only in-memory Domain objects/primitives and returns a
/// <see cref="Result{T}"/> - no EF Core, no I/O, fully unit-testable against the R1-R10 fixtures
/// with no database.
/// </summary>
public interface IApprovalRoutingService
{
    Result<ApprovalChainResolution> Resolve(ApprovalRoutingRequest request);
}
