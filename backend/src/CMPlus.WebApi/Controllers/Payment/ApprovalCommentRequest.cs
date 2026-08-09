namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>Request body shared by <c>approve</c>/<c>return-for-revision</c>/<c>reject</c>
/// (design.md §2.3: identical <c>{ "comment": "…" }</c> shape for all three). Whether
/// <see cref="Comment"/> is actually mandatory is a per-action rule enforced by each command's own
/// validator (mandatory for reject/return-for-revision, optional for approve) - not by this shared
/// wire DTO.</summary>
public sealed record ApprovalCommentRequest(string? Comment);
