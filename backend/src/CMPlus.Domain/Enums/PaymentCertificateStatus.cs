namespace CMPlus.Domain.Enums;

/// <summary>
/// Payment Certificate (IPC) state machine (approval-workflow.md §3). Seven values - has a
/// <see cref="NotDue"/> precursor before <c>Draft</c> and a <see cref="Paid"/> state after the
/// chain's success terminal (<see cref="Certified"/>) that VariationOrder does not have.
/// </summary>
/// <remarks>
/// docs/10 §5 (P0-BE-02) names a single shared "ApprovalStatus" enum, but docs/10 §3's data
/// model table gives <c>VariationOrder.Status</c> 5 values and <c>PaymentCertificate.Status</c>
/// 7 values with different terminology for the success terminal (VO: "Approved", IPC:
/// "Certified") - the two state machines are not simply different-length prefixes of one
/// ordering. A single shared enum would let either aggregate be set to a status meaningless for
/// its own state machine (e.g. a VariationOrder in "Paid"). Implemented here as two separate
/// enums, <see cref="VariationOrderStatus"/> and <see cref="PaymentCertificateStatus"/>, each
/// matching its own document's transition table exactly. Flagged for system-architect
/// confirmation - see the handoff report.
/// </remarks>
public enum PaymentCertificateStatus
{
    NotDue = 1,
    Draft = 2,
    PendingApproval = 3,
    Certified = 4,
    Paid = 5,
    Rejected = 6,
    Cancelled = 7,
}
