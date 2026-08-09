using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.Projects;

/// <summary>Request body for `PUT /api/v1/projects/{projectId}/eac-default` (design.md §2.1:
/// `{ "variant": "CpiSpiBased" }`). <see cref="Variant"/> binds directly against the
/// <see cref="EacVariant"/> enum via the globally-registered `JsonStringEnumConverter` (Program.cs) -
/// an unrecognised string fails body deserialization with a standard 400 before this action even
/// runs (there is no bespoke "invalid variant" contract for this endpoint, unlike `GET .../evm`'s
/// explicit `invalid-eac-variant` type).</summary>
public sealed record SetEacVariantDefaultRequest(EacVariant Variant);
