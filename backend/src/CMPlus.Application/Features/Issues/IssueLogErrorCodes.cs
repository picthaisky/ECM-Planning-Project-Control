namespace CMPlus.Application.Features.Issues;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for S11-BE-03's
/// <c>CreateIssueCommand</c>/<c>AdvanceIssueStatusCommand</c>/<c>ListIssuesQuery</c>
/// (domain-rules.md weather-eot §9).</summary>
public static class IssueLogErrorCodes
{
    public const string ProjectNotFound = "IssueLogProjectNotFound";

    /// <summary>Sprint 10's L-01 fix pattern (this task's brief): fail closed on a null actor id
    /// rather than fabricating <c>Guid.Empty</c>. Maps to 401.</summary>
    public const string ActorRequired = "IssueLogActorRequired";

    /// <summary>No <see cref="CMPlus.Domain.Entities.IssueLog"/> with this id exists in the caller's
    /// tenant (or at all) - the global EF query filter (ADR-0002) makes "wrong tenant" and "does not
    /// exist" indistinguishable, a bare 404 either way.</summary>
    public const string NotFound = "IssueLogNotFound";

    /// <summary>domain-rules.md §9.1/fixture W-12a: attempted to advance an issue that is already
    /// <c>Closed</c> (terminal - no reopen in this sprint's scope, Q11's ruling). Maps to 409.</summary>
    public const string AlreadyClosed = "IssueAlreadyClosed";

    /// <summary>domain-rules.md §9.2: a concurrent <c>advance-status</c> already moved this issue -
    /// the second caller gets 409, never a double-advance. Maps to 409.</summary>
    public const string ConcurrencyConflict = "IssueLogConcurrencyConflict";
}
