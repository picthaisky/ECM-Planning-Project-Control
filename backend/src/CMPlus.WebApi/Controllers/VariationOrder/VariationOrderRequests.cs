using CMPlus.Domain.Entities;

namespace CMPlus.WebApi.Controllers.VariationOrder;

/// <summary>Request body shared by <c>approve</c>/<c>return-for-revision</c>/<c>reject</c>/<c>cancel</c>
/// - identical shape to <c>CMPlus.WebApi.Controllers.Payment.ApprovalCommentRequest</c>, kept as its
/// own type so this controller vertical stays self-contained (same "each feature folder owns its own
/// wire DTOs" convention this codebase already follows). Whether <see cref="Comment"/> is actually
/// mandatory is a per-action rule enforced by each command's own validator, not by this shared
/// record.</summary>
public sealed record VoApprovalCommentRequest(string? Comment);

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/variation-orders</c>.</summary>
public sealed record CreateVariationOrderRequest(
    string VoNumber,
    string? Description,
    string? Justification,
    decimal Amount,
    int TimeImpactDays,
    IReadOnlyList<VariationOrderScopeItemInput> ScopeItems);

/// <summary>Request body for <c>PUT /api/v1/variation-orders/{id}/content</c> (re-pricing a
/// returned-for-revision document before resubmission, fixture V-10).</summary>
public sealed record UpdateVariationOrderContentRequest(
    string? Description,
    string? Justification,
    decimal Amount,
    int TimeImpactDays,
    IReadOnlyList<VariationOrderScopeItemInput> ScopeItems);
