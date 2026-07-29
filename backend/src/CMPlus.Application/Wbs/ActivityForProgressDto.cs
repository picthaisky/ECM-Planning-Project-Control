namespace CMPlus.Application.Wbs;

/// <summary>
/// One activity available for progress entry under a WBS node (S4-BE-05, fast-follow to close the
/// "no endpoint to list activities under a node" gap - `GetWbsTreeQuery` only ever returns a
/// node-level <c>ActivityCount</c> rollup, by that query's own design). Field names/shape match
/// `web/src/features/wbs/types.ts`'s <c>ActivityForProgress</c> exactly (id/activityCode/name/
/// currentProgressPercentage) - the frontend already built its batch-progress grid against this
/// contract as "the endpoint that should exist" (see `getNodeActivities` in `features/wbs/api.ts`),
/// so this DTO is shaped to match that existing expectation rather than inventing a different one
/// that would force another frontend round trip.
/// </summary>
public sealed record ActivityForProgressDto(
    Guid Id,
    string ActivityCode,
    string Name,
    decimal CurrentProgressPercentage);
