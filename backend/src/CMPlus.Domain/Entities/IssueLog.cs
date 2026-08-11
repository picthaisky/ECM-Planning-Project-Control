using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Site issue / action-log entry (S11-BE-03, US-11.2). State machine and field shape per
/// <c>docs/specs/weather-eot/domain-rules.md</c> §9 (landed mid-implementation; reconciled here).
/// Ordinary mutable aggregate - unlike <see cref="DailyWeatherLog"/> it is NOT
/// <see cref="IAppendOnly"/>: an issue's whole purpose is to be tracked to resolution, so
/// <see cref="Status"/> legitimately changes in place.
///
/// <para><b>Not modelled here (disclosed, not silently dropped):</b> a persisted, gap-free,
/// per-project <c>IssueNo</c> display sequence needs either a database identity scoped per
/// <c>ProjectId</c> (SQL Server <c>IDENTITY</c> is table-wide, not partitionable) or an app-level
/// counter-with-retry - a real concurrency design decision, not a data-capture one, so it is
/// deliberately not guessed here. <c>ListIssuesQueryHandler</c> instead derives a presentational,
/// non-persisted <c>SequenceNo</c> (oldest = 1) purely for display. <c>RelatedWeatherLogId</c>
/// (domain-rules.md §9.4, described there as "a reasonable convenience", not mandatory) and
/// <c>RelatedIssueId</c> (§9.1's recurrence path for a reopened issue) are also not modelled this
/// pass - no code path creates either relationship yet since reopening is not implemented
/// (Q11's ruling: closed is terminal).</para>
/// </summary>
public sealed class IssueLog : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    /// <summary>Free-text responsible party ("ผู้รับผิดชอบ" - e.g. "จัดซื้อ", "Safety",
    /// "วิศวกรโครงสร้าง"). Deliberately not a <c>Guid</c> FK to <see cref="User"/>: the prototype's
    /// own fixture data assigns issues to roles/departments, not named system accounts, and forcing
    /// every owner to be a registered user is a real product decision this task does not make.</summary>
    public string? Owner { get; private set; }

    public DateTimeOffset? DueDate { get; private set; }

    public IssueStatus Status { get; private set; }

    /// <summary>Stamped exactly once, on entry to <see cref="IssueStatus.Doing"/> (domain-rules.md
    /// §9.2, "recommended addition", symmetric with <see cref="ClosedAt"/>) - enables cycle-time
    /// reporting later. Never touched again afterward.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Stamped exactly once, only on the transition INTO <see cref="IssueStatus.Closed"/>
    /// (S11-BE-03 DoD) - never touched again afterward, since <see cref="Closed"/> is terminal.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (domain-rules.md §9.2:
    /// "two users advancing simultaneously means the second gets 409, never a double-advance - the
    /// same discipline as the approval handlers"). Two site users tapping "advance" on the same
    /// issue within the same request window is a realistic concurrent-edit window per
    /// db-conventions.md §4's policy, unlike the earlier read of this entity as low-contention.</summary>
    public byte[] RowVersion { get; private set; } = [];

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private IssueLog()
    {
    }

    public IssueLog(
        Guid tenantId,
        Guid projectId,
        string title,
        string? detail,
        string? owner,
        DateTimeOffset? dueDate,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("IssueLog.ProjectId is required.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new DomainException("IssueLog.CreatedByUserId is required.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        Title = ValidateTitle(title);
        Detail = detail;
        Owner = owner;
        DueDate = dueDate;
        Status = IssueStatus.Open;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The only writer of <see cref="Status"/>/<see cref="StartedAt"/>/<see cref="ClosedAt"/> -
    /// <c>Open -&gt; Doing -&gt; Closed</c>, exactly one step per call (S11-BE-03 DoD; domain-rules.md
    /// §9.1 Q10's ruling: skipping <c>Doing</c> is not permitted). Throws once already
    /// <see cref="IssueStatus.Closed"/> - there is no further transition (Q11's ruling: no reopen;
    /// a recurrence is a new issue).
    /// </summary>
    public void AdvanceStatus(DateTimeOffset actedAt)
    {
        switch (Status)
        {
            case IssueStatus.Open:
                Status = IssueStatus.Doing;
                StartedAt = actedAt;
                break;

            case IssueStatus.Doing:
                Status = IssueStatus.Closed;
                ClosedAt = actedAt;
                break;

            case IssueStatus.Closed:
                throw new DomainException("IssueAlreadyClosed: cannot advance an issue that is already Closed.");

            default:
                throw new DomainException($"Unknown IssueStatus '{Status}'.");
        }
    }

    private static string ValidateTitle(string title) =>
        string.IsNullOrWhiteSpace(title)
            ? throw new DomainException("IssueLog.Title is required.")
            : title.Trim();
}
