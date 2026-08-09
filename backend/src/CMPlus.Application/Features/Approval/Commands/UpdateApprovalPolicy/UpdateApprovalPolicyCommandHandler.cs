using System.Text.RegularExpressions;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;

public sealed partial class UpdateApprovalPolicyCommandHandler(
    IApprovalPolicyRepository repository, ITenantProvider tenantProvider, IDateTimeProvider clock)
    : IRequestHandler<UpdateApprovalPolicyCommand, Result<ApprovalPolicyDto>>
{
    public async Task<Result<ApprovalPolicyDto>> Handle(UpdateApprovalPolicyCommand request, CancellationToken cancellationToken)
    {
        // Additive pre-check ahead of ApprovalPolicy's own (Sprint 2, already-tested) band
        // validation - see ApprovalPolicyErrorCodes.BandOverlapPrefix's remarks for why this specific
        // shape of invalid input is not otherwise caught.
        if (FindOverlappingStepNo(request.Rules) is { } overlapStepNo)
        {
            return Result<ApprovalPolicyDto>.Failure($"{ApprovalPolicyErrorCodes.BandOverlapPrefix}{overlapStepNo}");
        }

        var current = await repository.FindActiveTenantDefaultAsync(request.DocumentType, cancellationToken);
        var now = clock.UtcNow;

        ApprovalPolicy nextVersion;
        try
        {
            nextVersion = current is null
                ? ApprovalPolicy.CreateInitialVersion(
                    tenantProvider.TenantId, projectId: null, request.DocumentType, now, request.Rules,
                    request.AllowSelfApproval, request.CumulativeVoEscalationPct, request.CumulativeVoEscalationRole)
                : current.CreateNextVersion(
                    now, request.Rules, request.AllowSelfApproval,
                    request.CumulativeVoEscalationPct, request.CumulativeVoEscalationRole);
        }
        catch (DomainException ex)
        {
            return Result<ApprovalPolicyDto>.Failure(BuildBandGapErrorCode(ex));
        }

        // Never edits `current` in place beyond flipping IsActive/EffectiveTo - `nextVersion` is
        // always a brand-new row with its own new Id (approval-workflow.md §5.2 "version-pin, never
        // mutate"). Both persist in the same SaveChanges call (IApprovalPolicyRepository.SaveChangesAsync)
        // so a reader can never observe two simultaneously-active versions.
        current?.Deactivate(now);
        repository.AddVersion(nextVersion);

        await repository.SaveChangesAsync(cancellationToken);

        return Result<ApprovalPolicyDto>.Success(ToDto(nextVersion));
    }

    private static ApprovalPolicyDto ToDto(ApprovalPolicy policy)
    {
        var rules = policy.Rules
            .OrderBy(r => r.StepNo)
            .Select(r => new ApprovalPolicyRuleDto(r.StepNo, r.MinAmount, r.MaxAmount, r.RequiredRole, r.QuorumCount))
            .ToList();

        return new ApprovalPolicyDto(
            policy.DocumentType, policy.Version, policy.IsActive, policy.AllowSelfApproval,
            policy.CumulativeVoEscalationPct, policy.CumulativeVoEscalationRole, rules);
    }

    /// <summary>Two different rules claim the <b>same</b> <c>StepNo</c> over intersecting amount
    /// ranges - sorts each StepNo group by <c>MinAmount</c> and checks each adjacent pair for
    /// intersection (a standard sorted-interval-overlap check). Returns the first offending
    /// <c>StepNo</c>, or <see langword="null"/> when no such conflict exists.</summary>
    private static int? FindOverlappingStepNo(IReadOnlyList<ApprovalPolicyRuleInput> rules)
    {
        foreach (var group in rules.GroupBy(r => r.StepNo).OrderBy(g => g.Key))
        {
            var ordered = group.OrderBy(r => r.MinAmount).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var currentMax = ordered[i].MaxAmount;
                var nextMin = ordered[i + 1].MinAmount;

                // currentMax == null means unbounded - necessarily overlaps whatever comes next.
                if (currentMax is null || currentMax.Value > nextMin)
                {
                    return group.Key;
                }
            }
        }

        return null;
    }

    [GeneratedRegex("missing StepNo (\\d+)")]
    private static partial Regex MissingStepNoPattern();

    /// <summary>Extracts the offending <c>StepNo</c> straight out of <c>ApprovalPolicy.ValidateBands</c>'s
    /// own exception message (owned by this codebase, stable format) rather than re-implementing its
    /// gap-detection algorithm here - "one fact lives in one place" (conventions.md). Falls back to
    /// StepNo 0 ("unresolved") on the (defensive-only; the validator should already have rejected
    /// every other DomainException-triggering shape) chance the message format ever changes.</summary>
    private static string BuildBandGapErrorCode(DomainException ex)
    {
        var match = MissingStepNoPattern().Match(ex.Message);
        var stepNo = match.Success ? match.Groups[1].Value : "0";
        return $"{ApprovalPolicyErrorCodes.BandGapPrefix}{stepNo}";
    }
}
