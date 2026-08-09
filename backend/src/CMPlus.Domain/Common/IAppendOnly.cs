namespace CMPlus.Domain.Common;

/// <summary>
/// Marker for an entity whose rows must never be updated or deleted once written - legal/audit
/// evidence (<c>ApprovalAction</c>, <c>AuditLog</c>) or an accounting ledger (<c>ProjectFinanceLedger</c>,
/// <c>ActualCostEntry</c>, <c>EvmPeriodSnapshot</c>) where a correction must be a new compensating
/// row, never an edit in place (conventions.md, ADR-0008, ADR-0009, ADR-0013).
///
/// <para>Before this marker existed, "append-only" was a C#-API convention only: the entity had no
/// mutator method/public setter, but an ordinary <c>DbContext</c> could still rewrite or delete the
/// row directly via <c>context.Entry(...)</c>/<c>Remove(...)</c> - execution-verified in the Sprint 9
/// security review (S9-SEC-01 finding M-01, probe 7). The Infrastructure-layer save-changes
/// interceptor that enforces this marker throws when any <see cref="IAppendOnly"/> entity is
/// <c>Modified</c>/<c>Deleted</c> at <c>SavingChanges</c>, closing the gap structurally rather than
/// by discipline alone. Applying it is a per-entity opt-in (implement the interface), not automatic,
/// because plenty of entities - including this same aggregate's own mutable siblings, e.g.
/// <c>PaymentCertificateApprovalStep</c>, which a <c>ReturnForRevision</c>/<c>Withdraw</c>
/// legitimately clears and re-populates - must remain freely editable.</para>
/// </summary>
public interface IAppendOnly
{
}
