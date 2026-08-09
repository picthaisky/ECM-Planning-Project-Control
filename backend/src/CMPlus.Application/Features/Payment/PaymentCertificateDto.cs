using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Payment;

/// <summary>
/// Wire shape for a <see cref="PaymentCertificate"/>, shared by every S9-BE-05 command handler
/// (Submit/Approve/ReturnForRevision/Reject/RecordPayment) as its success response - "the current
/// representation of the resource", mirroring <c>ActualCostEntryDto</c>/<c>ProjectDto</c>'s own
/// <c>From(entity)</c> pattern.
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
    Guid CreatedByUserId,
    Guid? SubmittedByUserId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CertifiedAt,
    DateTimeOffset? PaidAt,
    string? PaymentReference)
{
    public static PaymentCertificateDto From(PaymentCertificate certificate) => new(
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
        certificate.CreatedByUserId,
        certificate.SubmittedByUserId,
        certificate.SubmittedAt,
        certificate.CertifiedAt,
        certificate.PaidAt,
        certificate.PaymentReference);
}
