namespace CMPlus.WebApi.Controllers.Evm;

/// <summary>Request body for `POST /api/v1/projects/{projectId}/evm/snapshots` (design.md §2.1:
/// `{ "dataDate": "…" }` - no variant override; a snapshot always freezes the project's own current
/// `EacVariantDefault`).</summary>
public sealed record CloseEvmPeriodRequest(DateTimeOffset DataDate);
