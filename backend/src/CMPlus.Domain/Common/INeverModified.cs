namespace CMPlus.Domain.Common;

/// <summary>
/// Marker for an entity whose rows may be <b>added</b> and <b>deleted as a whole set</b>, but must
/// never be <b>modified</b> in place. Strictly weaker than <see cref="IAppendOnly"/>, which also
/// forbids deletion.
///
/// <para>This exists for <c>PaymentCertificateApprovalStep</c> — the snapshot of the resolved
/// approval chain. It cannot be <see cref="IAppendOnly"/>, because <c>ReturnForRevision</c>/
/// <c>Withdraw</c> legitimately clear the whole snapshot so a resubmission can re-resolve a fresh
/// chain (the amount may have changed, so the old rungs must not survive). But nothing in
/// production ever edits an individual rung: every setter is private, there is no mutator method,
/// and <c>PaymentCertificate.Submit</c> is the only writer.</para>
///
/// <para>The distinction matters because, after the H-01 fix, that snapshot <em>is</em> the sole
/// record of who may approve which step — so a single tampered rung is a direct authority
/// escalation. The Sprint 9 re-verification (S9-SEC-02 finding N-01) proved it by rewriting a rung
/// through an ordinary <c>DbContext</c> and then having a <c>Site</c> user certify a ฿9,000,000
/// certificate the tenant's delegation-of-authority reserved for a Project Director. That was not
/// reachable through any handler — so it is defence in depth, not a live exploit — but "no handler
/// happens to expose it today" is a weaker guarantee than the code can actually offer, and this
/// marker closes the gap without costing the legitimate clear-and-rebuild lifecycle anything.</para>
/// </summary>
public interface INeverModified
{
}
