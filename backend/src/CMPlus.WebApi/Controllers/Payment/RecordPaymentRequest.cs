namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>Request body for <c>POST /api/v1/payment-certificates/{id}/record-payment</c>
/// (design.md §2.3: <c>{ "reference": "…", "paidAt": "…" }</c>).</summary>
public sealed record RecordPaymentRequest(string Reference, DateTimeOffset PaidAt);
