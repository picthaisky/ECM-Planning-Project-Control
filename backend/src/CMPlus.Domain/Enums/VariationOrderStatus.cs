namespace CMPlus.Domain.Enums;

/// <summary>
/// Variation Order state machine (approval-workflow.md §2). Five values -
/// <c>Approved</c>/<c>Rejected</c>/<c>Cancelled</c> are terminal; a superseding VO is always a
/// new record, never an edit of a terminal one. See <see cref="PaymentCertificateStatus"/> for
/// why this is a separate enum rather than a single shared "ApprovalStatus".
/// </summary>
public enum VariationOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
}
