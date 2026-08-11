namespace CMPlus.Application.Features.Manpower;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for S12-BE-02's
/// <c>RecordManpowerLogCommand</c>/<c>RecordManpowerLogCorrectionCommand</c>/
/// <c>GetProductivityIndexQuery</c> (domain-rules.md (manpower-equipment) §4/§5).</summary>
public static class ManpowerLogErrorCodes
{
    public const string ProjectNotFound = "ManpowerLogProjectNotFound";

    /// <summary>Sprint 10's L-01 fix pattern (this task's brief): the caller has no resolvable user
    /// id (<c>ICurrentUserContext.UserId</c> is null). Structurally unreachable behind
    /// <c>[Authorize]</c> in production; kept as a fail-closed guard rather than ever stamping
    /// <c>Guid.Empty</c> onto <c>RecordedByUserId</c>. Maps to 401.</summary>
    public const string ActorRequired = "ManpowerLogActorRequired";

    /// <summary>§4.1: <c>WorkCategoryId</c> does not resolve in this tenant/project. Maps to 422.</summary>
    public const string WorkCategoryNotInProject = "ManpowerLogWorkCategoryNotInProject";

    /// <summary>§4.1: <c>WbsNodeId</c> resolves in this tenant but belongs to a different project.
    /// Distinct from <see cref="WbsNodeNotFound"/> (ADR-0002: a cross-tenant/unknown id is never
    /// distinguishable from "does not exist" - fixture M-14b). Maps to 422.</summary>
    public const string WbsNodeNotInProject = "ManpowerLogWbsNodeNotInProject";

    /// <summary>§4.1: <c>WbsNodeId</c> does not resolve in this tenant at all (cross-tenant or
    /// genuinely unknown - ADR-0002, fixture M-14b). Maps to 404.</summary>
    public const string WbsNodeNotFound = "ManpowerLogWbsNodeNotFound";

    /// <summary>§4.1: <c>ActivityId</c> resolves in this tenant but belongs to a different project.
    /// Maps to 422.</summary>
    public const string ActivityNotInProject = "ManpowerLogActivityNotInProject";

    /// <summary>§4.1: <c>ActivityId</c> does not resolve in this tenant at all (ADR-0002, fixture
    /// M-14b). Maps to 404.</summary>
    public const string ActivityNotFound = "ManpowerLogActivityNotFound";

    /// <summary>§4.1's last validation rule: <c>ActivityId</c> is set and its own <c>WbsNodeId</c>
    /// disagrees with the row's <c>WbsNodeId</c> (also set) - one row, one attribution. Maps to 422.</summary>
    public const string ActivityWbsNodeMismatch = "ManpowerLogActivityWbsNodeMismatch";

    /// <summary>§4.4/Q8: an in-force Original already exists for the same natural key
    /// (LogDate, Shift, WorkCategoryId, WbsNodeId, LabourType, SubcontractorRef) and the request did
    /// not carry <c>allowDuplicate: true</c>. Maps to 409.</summary>
    public const string AlreadyExists = "ManpowerLogAlreadyExists";

    /// <summary>§4.7 chain-integrity rule 1: <c>CorrectsLogId</c> (taken from the route, never the
    /// body) does not resolve to an existing <see cref="CMPlus.Domain.Entities.ManpowerEquipmentLog"/>
    /// in this project. Maps to 422.</summary>
    public const string CorrectionTargetNotFound = "ManpowerLogCorrectionTargetNotFound";

    /// <summary>§4.7 chain-integrity rule 2 (the load-bearing one): the target already has an entry
    /// pointing at it - a correction must target the current chain tail, not an already-superseded
    /// entry. Maps to 409.</summary>
    public const string AlreadySuperseded = "ManpowerLogAlreadySuperseded";

    /// <summary>§4.7 chain-integrity rule 4: the target must be strictly older
    /// (<c>target.RecordedAt &lt; this.RecordedAt</c>). Maps to 422.</summary>
    public const string CorrectionOrdering = "ManpowerLogCorrectionOrdering";

    /// <summary>§4.7: there is no <c>PUT</c>/<c>PATCH</c>/<c>DELETE</c> route for a manpower log - a
    /// deliberate 405 pointing the caller at the corrections endpoint. Maps to 405.</summary>
    public const string IsImmutable = "ManpowerLogIsImmutable";

    /// <summary>§5's PI read: the requested 'from'/'to' window is malformed. Maps to 400.</summary>
    public const string InvalidDateRange = "ManpowerLogInvalidDateRange";
}
