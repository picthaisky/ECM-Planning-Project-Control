using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One saved reference point for a project's schedule/budget (US-14.1, docs/10 §9 S14-BE-01),
/// snapshotting every <see cref="Activity"/>'s planned dates/duration/budget at the moment it was
/// captured (<see cref="BaselineActivitySnapshot"/>) so the delta-comparison query
/// (S14-BE-02) can compute a current-vs-baseline variance later, even after the live schedule has
/// moved on.
///
/// <para><b>Exactly one active baseline per project (docs/10 §9 S14-BE-01 DoD: "หนึ่งโครงการมี
/// IsActive=true ได้ชุดเดียว (บังคับที่ระดับ DB ด้วย)").</b> Enforced at two independent layers,
/// mirroring <see cref="ApprovalPolicy"/>'s identical shape (ADR-0008, `(TenantId, ProjectId,
/// DocumentType) WHERE IsActive = 1`): (1) the Application handler
/// (<c>ActivateBaselineCommandHandler</c>, via <c>IBaselineRepository.TryActivateAsync</c>)
/// deactivates whichever baseline is currently active for the project before activating the target,
/// and (2) a DB-level filtered unique index (<c>BaselineConfiguration</c>: `(TenantId, ProjectId)
/// WHERE IsActive = 1`) makes a second simultaneously-active row impossible on a real database even
/// if the application-layer half is ever bypassed. <b>S14-BE-01 defect fix
/// (docs/perf/s14-baseline-storage.md §2):</b> the original shape flushed both writes together in one
/// <c>SaveChanges</c> batch, relying on EF Core emitting the deactivate UPDATE before the activate
/// UPDATE - the same assumption <c>security-auditor</c> flagged as unverified for
/// <see cref="ApprovalPolicy"/>. <c>database-engineer</c>'s 30-trial SQLite probe against this exact
/// entity/configuration proved that assumption false for this handler specifically (a ~50/50 split,
/// because this handler's tracking order for the two <c>Modified</c> siblings is inverted relative to
/// <see cref="ApprovalPolicy"/>'s <c>Modified</c>+<c>Added</c> pair - see
/// <c>ActivateBaselineCommandHandler</c>'s remarks for why). The fix persists the deactivate and the
/// activate as two separate, sequential <c>SaveChangesAsync</c> calls inside one transaction instead
/// (verified 30/30 on SQLite) - see <c>IBaselineRepository.TryActivateAsync</c>'s remarks. The EF Core
/// InMemory provider this environment is limited to still does not enforce unique indexes at all, so
/// no test run in this environment can observe statement ordering directly either way; the ordering
/// proof is the SQLite probe, not any test in this repository.</para>
///
/// <para><b>Not <see cref="IAppendOnly"/> (unlike its <see cref="BaselineActivitySnapshot"/>
/// children - see that type's own remarks): <see cref="IsActive"/> must remain mutable</b> for the
/// entire life of the project (a PM can flip which baseline is the active reference point at any
/// time), which is exactly the state change <see cref="IAppendOnly"/>/<see cref="INeverModified"/>
/// both forbid. Every other field is set once at <see cref="Capture"/> and never exposed for edit -
/// only <see cref="IsActive"/> has a mutator (<see cref="Activate"/>/<see cref="Deactivate"/>).</para>
/// </summary>
public sealed class Baseline : Entity, ITenantOwned
{
    private readonly List<BaselineActivitySnapshot> _snapshots = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Never <see cref="Guid.Empty"/> - <c>CaptureBaselineCommandHandler</c> fails closed
    /// (<c>BaselineErrorCodes.ActorRequired</c>, 401) on a null <c>ICurrentUserContext.UserId</c>
    /// rather than ever constructing a Baseline with a fabricated actor (the same "never fabricate
    /// an actor" discipline <see cref="CpmRun.TriggeredByUserId"/>'s remarks describe, applied here
    /// as NOT NULL rather than nullable because - unlike a <c>CpmRun</c> - there is no
    /// system-triggered capture path; every baseline is captured by a human action).</summary>
    public Guid CapturedByUserId { get; private set; }

    /// <summary><c>Project.BAC</c> at the moment of capture - descriptive/historical only (the
    /// management table's "BAC (MB)" column, prototype "จัดการ Baseline"), never re-derived or kept
    /// in sync with the live project afterward.</summary>
    public decimal Bac { get; private set; }

    public IReadOnlyList<BaselineActivitySnapshot> Snapshots => _snapshots.AsReadOnly();

    public int ActivityCount => _snapshots.Count;

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private Baseline()
    {
    }

    private Baseline(
        Guid tenantId,
        Guid projectId,
        string name,
        DateTimeOffset capturedAt,
        Guid capturedByUserId,
        decimal bac,
        IReadOnlyCollection<BaselineActivitySnapshotInput> snapshots)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("Baseline.ProjectId is required.");
        }

        if (capturedByUserId == Guid.Empty)
        {
            throw new DomainException("Baseline.CapturedByUserId is required.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        Name = ValidateRequired(name, nameof(Name));
        IsActive = false;
        CapturedAt = capturedAt;
        CapturedByUserId = capturedByUserId;
        Bac = MoneyGuard.EnsureNonNegative(bac, nameof(Bac));

        foreach (var snapshot in snapshots)
        {
            _snapshots.Add(new BaselineActivitySnapshot(tenantId, Id, snapshot));
        }
    }

    /// <summary>The only way to obtain an instance - one snapshot of every activity supplied, taken
    /// together (docs/10 §9: "snapshot กิจกรรมทั้งหมด"). Never active on creation - a second,
    /// deliberate <see cref="Activate"/> call is required (mirrors the prototype's two separate
    /// actions, "+ บันทึก Baseline ใหม่" vs "ตั้งเป็น Active" - see
    /// <c>CaptureBaselineCommandHandler</c>'s remarks for why capture and activation are not
    /// combined into one operation). An empty <paramref name="snapshots"/> collection is legitimate
    /// (a project with zero activities yet still captures a valid, empty baseline - mirrors
    /// <see cref="CpmRun.Capture"/>'s identical allowance).</summary>
    public static Baseline Capture(
        Guid tenantId,
        Guid projectId,
        string name,
        DateTimeOffset capturedAt,
        Guid capturedByUserId,
        decimal bac,
        IReadOnlyCollection<BaselineActivitySnapshotInput> snapshots) =>
        new(tenantId, projectId, name, capturedAt, capturedByUserId, bac, snapshots);

    /// <summary>Setting <see cref="IsActive"/> to a value it already holds is a legitimate no-op:
    /// EF Core's change tracker only marks a property <c>IsModified</c> when the value actually
    /// differs from what was loaded, so re-activating an already-active baseline never issues a
    /// spurious <c>UPDATE</c>. <c>ActivateBaselineCommandHandler</c> additionally short-circuits
    /// before calling this at all when the target is already active, purely to skip the extra
    /// "find the previous active baseline" read - not because calling this twice would be unsafe.
    /// </summary>
    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string ValidateRequired(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{propertyName} is required.")
            : value.Trim();
}

/// <summary>Plain data shape for one activity's captured state supplied to <see cref="Baseline.Capture"/>
/// - mirrors <see cref="CpmRunActivityInput"/>'s "input shape before it becomes a child entity"
/// pattern. Field set matches docs/10 §9's explicit <c>BaselineActivitySnapshot</c> list exactly
/// (Id, TenantId, BaselineId, ActivityId, PlannedStart/Finish, DurationDays, BudgetCost) - no
/// criticality/float fields, since the source design does not list them (see the
/// backend-developer report on the "Critical Path เปลี่ยนเส้นทาง" prototype tile this leaves
/// unreconstructable from this schema).</summary>
public sealed record BaselineActivitySnapshotInput(
    Guid ActivityId,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedFinish,
    int DurationDays,
    decimal BudgetCost);
