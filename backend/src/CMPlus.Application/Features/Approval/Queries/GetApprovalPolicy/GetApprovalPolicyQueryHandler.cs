using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;

public sealed class GetApprovalPolicyQueryHandler(IApprovalPolicyReader reader)
    : IRequestHandler<GetApprovalPolicyQuery, Result<ApprovalPolicyDto>>
{
    private const string NotFound = "ApprovalPolicyNotFound";

    public async Task<Result<ApprovalPolicyDto>> Handle(GetApprovalPolicyQuery request, CancellationToken cancellationToken)
    {
        var policy = await reader.GetActiveTenantDefaultPolicyAsync(request.DocumentType, cancellationToken);
        if (policy is null)
        {
            return Result<ApprovalPolicyDto>.Failure(NotFound);
        }

        var rules = policy.Rules
            .OrderBy(r => r.StepNo)
            .Select(r => new ApprovalPolicyRuleDto(r.StepNo, r.MinAmount, r.MaxAmount, r.RequiredRole, r.QuorumCount))
            .ToList();

        var dto = new ApprovalPolicyDto(
            policy.DocumentType,
            policy.Version,
            policy.IsActive,
            policy.AllowSelfApproval,
            policy.CumulativeVoEscalationPct,
            policy.CumulativeVoEscalationRole,
            rules);

        return Result<ApprovalPolicyDto>.Success(dto);
    }
}
