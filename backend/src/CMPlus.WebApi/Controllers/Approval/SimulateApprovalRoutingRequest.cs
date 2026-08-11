namespace CMPlus.WebApi.Controllers.Approval;

/// <summary>Request body for <c>POST /api/v1/tenants/{tenantId}/approval-policies/{documentType}/simulate</c>
/// (S15-BE-01).</summary>
public sealed record SimulateApprovalRoutingRequest(Guid ProjectId, decimal Amount);
