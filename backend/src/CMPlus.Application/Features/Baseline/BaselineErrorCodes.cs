namespace CMPlus.Application.Features.Baseline;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Baseline feature
/// (S14-BE-01/02). Mirrors the <c>CpmErrorCodes</c>/<c>ProjectErrorCodes</c> pattern.</summary>
public static class BaselineErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "BaselineProjectNotFound";

    /// <summary>The requested baseline does not exist, or exists but is not owned by the
    /// <c>projectId</c> in the route (which is treated identically to "does not exist" - bare 404,
    /// never a 422 that would confirm the id exists somewhere else, the same discipline
    /// ADR-0002/fixture M-14b already established for a cross-tenant id).</summary>
    public const string NotFound = "BaselineNotFound";

    /// <summary>S14-BE-02: the project exists and is well-formed, but has no currently-active
    /// baseline to compare against (no baseline has ever been captured, or every captured one has
    /// since been deliberately deactivated). A well-formed request the current project state cannot
    /// answer yet - maps to 422, the same shape as <c>EotErrorCodes.NoCpmRunAvailable</c>, never a
    /// 404 (the project itself was found).</summary>
    public const string NoActiveBaseline = "BaselineNoActiveBaseline";

    /// <summary>"Fail closed on a null actor - no <c>?? Guid.Empty</c>" (this task's standing
    /// requirement, the same L-01 discipline <c>CpmErrorCodes.ActorRequired</c>/
    /// <c>VariationOrderErrorCodes.ActorRequired</c> already apply): the caller has no resolvable
    /// user id (<c>ICurrentUserContext.UserId</c> is null) - <c>Baseline.CapturedByUserId</c> must
    /// never be fabricated. Structurally unreachable behind <c>[Authorize]</c> in production; kept
    /// as a fail-closed guard rather than ever stamping <see cref="Guid.Empty"/> onto an actor
    /// field. Maps to 401.</summary>
    public const string ActorRequired = "BaselineActorRequired";

    /// <summary>Someone else changed the project's active baseline between the read and the write -
    /// reachable via a <c>DbUpdateConcurrencyException</c> from <c>ActivateBaselineCommand</c>'s save
    /// (currently unreachable in practice, kept for symmetry - <see cref="Baseline"/> carries no
    /// <c>RowVersion</c>), and, since the S14-QA concurrent-activation-race fix, also via a genuine
    /// two-request race on the `(TenantId, ProjectId) WHERE IsActive = 1` filtered unique index: two
    /// concurrent activations of two <em>different</em> baselines for the same project, both having
    /// read the same pre-race active baseline before either committed - see
    /// <c>IBaselineRepository.TryActivateAsync</c>'s remarks and
    /// <c>Infrastructure.Persistence.UniqueIndexViolationClassifier</c> for the full mechanism and
    /// proof. Both shapes are reported identically to the caller (the actionable response is the
    /// same either way: reload and retry) - maps to 409.</summary>
    public const string ConcurrencyConflict = "BaselineConcurrencyConflict";
}
