using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Payment;

/// <summary>
/// One rung of <see cref="PaymentCertificateDto.ApprovalSteps"/> - the snapshotted
/// <see cref="PaymentCertificateApprovalStep"/> exactly as resolved at Submit time (security review
/// sprint-09.md H-01 fix). S9 read-side gap closure: this is what lets the UI show a truthful chain
/// bar (role-per-step) and know which action buttons the current user is actually entitled to,
/// instead of `chainPermissions.ts`'s documented conservative guess.
/// </summary>
public sealed record PaymentCertificateApprovalStepDto(int StepNo, UserRole RequiredRole, int QuorumCount);

/// <summary>
/// Wire shape for a <see cref="PaymentCertificate"/>, shared by every S9-BE-05 command handler
/// (Submit/Approve/ReturnForRevision/Reject/RecordPayment) as its success response - "the current
/// representation of the resource", mirroring <c>ActualCostEntryDto</c>/<c>ProjectDto</c>'s own
/// <c>From(entity)</c> pattern - and by the S9 read-side gap closure's <c>GetPaymentCertificateQuery</c>/
/// <c>ListPaymentCertificatesQuery</c> (finding L-04).
/// </summary>
public sealed record PaymentCertificateDto(
    Guid Id,
    Guid ProjectId,
    int MilestoneNo,
    string? Description,
    decimal MilestoneValue,
    decimal PreviousCumulativeApprovePct,
    decimal ApprovePct,
    decimal? ClaimPct,
    decimal? ActualProgressPct,
    decimal GrossCertifiedAmount,
    decimal RetentionAmount,
    decimal AdvanceRecoveryAmount,
    decimal NetPayment,
    PaymentCertificateStatus Status,
    int RevisionNo,
    int CurrentStepNo,
    int TotalSteps,
    Guid? ApprovalPolicyId,
    int? ApprovalPolicyVersion,
    bool AllowSelfApproval,
    IReadOnlyList<PaymentCertificateApprovalStepDto> ApprovalSteps,
    int? CurrentStepApprovalsCollected,
    Guid CreatedByUserId,
    Guid? SubmittedByUserId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CertifiedAt,
    DateTimeOffset? PaidAt,
    string? PaymentReference)
{
    /// <summary>
    /// <paramref name="currentStepApprovalsCollected"/> is the S9 read-side gap closure's quorum-
    /// progress figure ("N of M signatures collected" on <see cref="PaymentCertificate.CurrentStepNo"/>) -
    /// optional and defaulting to <see langword="null"/> so the five existing S9-BE-05 command
    /// handlers (already security-cleared, deliberately left untouched by this gap closure) keep
    /// compiling and behaving identically without passing it. <see langword="null"/> means "not
    /// computed for this response" (e.g. every existing mutating-command response today, and
    /// <c>ListPaymentCertificatesQueryHandler</c> - see its own remarks for why) - deliberately
    /// distinct from a real <c>0</c> ("a chain is attached and genuinely nobody has approved the
    /// current step yet"), the same "unknown vs. genuinely zero" discipline
    /// <c>IActualCostReader</c>'s <c>ActualCostResult</c> already established (ADR-0013(f)). Only
    /// <c>GetPaymentCertificateQueryHandler</c> supplies a real value today.
    /// </summary>
    public static PaymentCertificateDto From(PaymentCertificate certificate, int? currentStepApprovalsCollected = null) => new(
        certificate.Id,
        certificate.ProjectId,
        certificate.MilestoneNo,
        certificate.Description,
        certificate.MilestoneValue,
        certificate.PreviousCumulativeApprovePct,
        certificate.ApprovePct,
        certificate.ClaimPct,
        certificate.ActualProgressPct,
        certificate.GrossCertifiedAmount,
        certificate.RetentionAmount,
        certificate.AdvanceRecoveryAmount,
        certificate.NetPayment,
        certificate.Status,
        certificate.RevisionNo,
        certificate.CurrentStepNo,
        certificate.TotalSteps,
        certificate.ApprovalPolicyId,
        certificate.ApprovalPolicyVersion,
        certificate.AllowSelfApproval,
        certificate.ApprovalSteps
            .OrderBy(s => s.StepNo)
            .Select(s => new PaymentCertificateApprovalStepDto(s.StepNo, s.RequiredRole, s.QuorumCount))
            .ToList(),
        currentStepApprovalsCollected,
        certificate.CreatedByUserId,
        certificate.SubmittedByUserId,
        certificate.SubmittedAt,
        certificate.CertifiedAt,
        certificate.PaidAt,
        certificate.PaymentReference);
}
