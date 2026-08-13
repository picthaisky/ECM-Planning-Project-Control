namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>
/// Body for <c>POST /api/v1/projects/{projectId}/payment-certificates</c> (S9-BE-05 create). The
/// project id is route-bound (not in the body); <c>PreviousCumulativeApprovePct</c> is deliberately
/// absent - the server auto-derives it from the project's prior certified certificates for this same
/// milestone (never a client input, so a client cannot understate the prior floor to inflate a claim).
/// </summary>
public sealed record CreatePaymentCertificateRequest(
    int MilestoneNo,
    string? Description,
    decimal MilestoneValue,
    decimal ThisCumulativeApprovePct,
    decimal? ClaimPct,
    decimal? ActualProgressPct,
    decimal? ManualAdvanceRecoveryAmount);
