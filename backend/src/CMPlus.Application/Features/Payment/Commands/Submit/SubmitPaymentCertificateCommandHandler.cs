using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Submit;

public sealed class SubmitPaymentCertificateCommandHandler(
    IPaymentCertificateRepository repository,
    IApprovalPolicyReader policyReader,
    IApprovalRoutingService routingService,
    IApprovalActionRepository actionRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SubmitPaymentCertificateCommand, Result<PaymentCertificateDto>>
{
    public async Task<Result<PaymentCertificateDto>> Handle(SubmitPaymentCertificateCommand request, CancellationToken cancellationToken)
    {
        var certificate = await repository.FindAsync(request.PaymentCertificateId, cancellationToken);
        if (certificate is null)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.NotFound);
        }

        if (certificate.Status != PaymentCertificateStatus.Draft)
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.InvalidStatusForTransition);
        }

        var now = clock.UtcNow;

        // approval-workflow.md §5.1: A^route for a Payment Certificate is G_k, already exactly
        // GrossCertifiedAmount (CertificateCalculator/S9-BE-02 already computed it non-negative) -
        // no abs() or further transform needed here, unlike a VariationOrder's signed Amount.
        var candidatePolicies = await policyReader.GetCandidatePoliciesAsync(
            ApprovalDocumentType.PaymentCertificate, now, cancellationToken);

        var routingResult = routingService.Resolve(new ApprovalRoutingRequest(
            ApprovalDocumentType.PaymentCertificate,
            certificate.ProjectId,
            certificate.GrossCertifiedAmount,
            now,
            candidatePolicies));

        if (routingResult.IsFailure)
        {
            // ApprovalErrorCodes.PolicyGap - 422, already mapped by ResultProblemMapper. Fail
            // closed: the certificate is left in Draft, never auto-approved.
            return Result<PaymentCertificateDto>.Failure(routingResult.Error);
        }

        var chain = routingResult.Value;
        var actorUserId = currentUser.UserId ?? Guid.Empty;

        // H-01 fix (security review sprint-09.md): snapshot the resolved chain - exactly as the
        // routing engine produced it for THIS document's routing amount - onto the certificate
        // itself, rather than handing it only a bare step count and leaving Approve/Reject/
        // ReturnForRevision to re-derive "what role does step N require" from the pinned policy's
        // entire rule set later (which is what dropped the amount-band filter and let an
        // out-of-band role authorize the wrong step).
        var resolvedSteps = chain.Steps
            .Select(step => new PaymentCertificateApprovalStepInput(step.StepNo, step.RequiredRole, step.QuorumCount))
            .ToList();

        certificate.Submit(resolvedSteps, chain.ApprovalPolicyId, chain.ApprovalPolicyVersion, chain.AllowSelfApproval, actorUserId, now);

        // Submit is not a decision against any one chain step (it is what creates the chain), so
        // StepNo is 0 - the same "not step-specific" convention ApprovalAction's own remarks give
        // for Cancel.
        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.PaymentCertificate,
            certificate.Id,
            certificate.RevisionNo,
            stepNo: 0,
            actorUserId,
            currentUser.Role,
            ApprovalActionType.Submit,
            comment: null,
            now,
            chain.ApprovalPolicyId,
            chain.ApprovalPolicyVersion));

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<PaymentCertificateDto>.Failure(PaymentApprovalErrorCodes.ConcurrencyConflict);
        }

        return Result<PaymentCertificateDto>.Success(PaymentCertificateDto.From(certificate));
    }
}
