namespace CMPlus.Domain.Enums;

/// <summary>
/// The human acts recorded on the append-only <c>ApprovalAction</c> ledger (ADR-0008), one
/// row per transition of the Variation Order / Payment Certificate state machines
/// (approval-workflow.md §4).
/// </summary>
public enum ApprovalActionType
{
    Submit = 1,
    Approve = 2,
    ReturnForRevision = 3,
    Reject = 4,
    Withdraw = 5,
    Cancel = 6,
    RecordPayment = 7,
}
