using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Daily manpower/equipment site log (S12-BE-02, US-12.2) - the append-only source of "actual
/// man-hours" (AMH) behind the Productivity Index (PI). Shape and correction semantics per
/// <c>docs/specs/manpower-equipment/domain-rules.md</c> §4 - this is the authoritative domain
/// artifact this task was told to implement exactly, not re-derive.
///
/// <para><b>Immutable by construction</b>, the identical three-layer defence
/// <see cref="DailyWeatherLog"/> established (domain-rules.md §4.7, reusing that pattern verbatim):
/// <see cref="IAppendOnly"/> so <see cref="Infrastructure.Persistence.Interceptors.AppendOnlyGuardInterceptor"/>
/// throws on any <c>Modified</c>/<c>Deleted</c> attempt; no mutator method or public setter exists
/// anywhere on this type; and the WebApi controller answers <c>PUT</c>/<c>PATCH</c>/<c>DELETE</c>
/// with a deliberate 405. A correction is always a brand-new row via
/// <see cref="CreateCorrection"/>/<see cref="CreateRetraction"/>, never an edit.</para>
///
/// <para><b>Supersession is a forward pointer only (§4.7) - never stamped on the original.</b>
/// <see cref="CorrectsLogId"/> points backward from a <see cref="ManpowerLogEntryKind.Correction"/>/
/// <see cref="ManpowerLogEntryKind.Retraction"/> row to the row it replaces/voids; the original is
/// never touched. The effective log set (<c>M^eff</c>, §4.7) is computed at read time: a row is "in
/// force" iff nothing points at it and it is not itself a <see cref="ManpowerLogEntryKind.Retraction"/>.
/// Chain-integrity rules (at most one entry may point at a given entry; a correction must target the
/// current chain tail; the target must be older) are enforced at the Application layer, where a DB
/// read is possible - see <c>RecordManpowerLogCorrectionCommandHandler</c>.</para>
///
/// <para><b>PlannedManCount deliberately does not live here</b> (§4.6(a)): a plan is a mutable,
/// revisable thing and this row is not - see <see cref="ManpowerPlan"/> instead. This entity also
/// never carries <see cref="Entities.ActualCostEntry"/>/money of any kind (§3): PI is an hours-only,
/// money-free indicator, and this log is not a source of AC in Sprint 12.</para>
/// </summary>
public sealed class ManpowerEquipmentLog : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Calendar-day identity, project timezone, 00:00-normalised by the caller (§4.1).</summary>
    public DateTimeOffset LogDate { get; private set; }

    public Shift Shift { get; private set; }

    /// <summary>หมวดงาน - required on every row (§4.1); never the free-text the prototype used (§4.3).</summary>
    public Guid WorkCategoryId { get; private set; }

    /// <summary>Control account. <see langword="null"/> = project-level / unattributed (§4.1).</summary>
    public Guid? WbsNodeId { get; private set; }

    /// <summary>Only when genuinely known (§4.1); when set, its own <c>WbsNodeId</c> must equal
    /// this row's <see cref="WbsNodeId"/> when that is also set - enforced at the Application layer
    /// (§4.1's last validation rule), since a DB lookup is needed to know the activity's node.</summary>
    public Guid? ActivityId { get; private set; }

    public LabourType LabourType { get; private set; }

    /// <summary>ผู้รับเหมาช่วง, when <see cref="LabourType"/> is not <see cref="LabourType.OwnDirect"/>.</summary>
    public string? SubcontractorRef { get; private set; }

    /// <summary>The prototype's "คน" - a headcount, never itself the productivity denominator (§4.1: "a
    /// head is not a unit of work").</summary>
    public int WorkerCount { get; private set; }

    /// <summary><c>decimal(9,2)</c>, the actual man-hours (AMH) denominator (§4.2). Need not equal
    /// <see cref="WorkerCount"/> × H under <c>Explicit</c> capture mode (part-days, OT, staggered
    /// shifts).</summary>
    public decimal ManHours { get; private set; }

    /// <summary>Subset of <see cref="ManHours"/>, never an addition (§4.1) - explains the rate half
    /// of a PI/CPI divergence (domain-rules.md §7.2).</summary>
    public decimal OvertimeHours { get; private set; }

    /// <summary><see langword="true"/> = <see cref="ManHours"/> was computed
    /// (<see cref="WorkerCount"/> × <c>ProjectEotPolicy.FullDayHours</c>) at write time, not
    /// independently measured (§4.2). Must be visible on screen - "a derived hour is an assumption".</summary>
    public bool ManHoursDerived { get; private set; }

    public int EquipmentCount { get; private set; }

    /// <summary><c>decimal(9,2)</c> - hours a unit was actually working (§2 EOH).</summary>
    public decimal EquipmentOperatingHours { get; private set; }

    /// <summary><c>decimal(9,2)</c> - hours a unit was on site, charged, and not working: idle,
    /// waiting, under repair (§2 ESH).</summary>
    public decimal EquipmentStandbyHours { get; private set; }

    /// <summary>The prototype's "โครงสร้าง ชั้น 9 + Curtain Wall" - free text, deliberately never the
    /// PI-matching key (§4.3).</summary>
    public string? WorkDescription { get; private set; }

    /// <summary>§9.4: annotation only, zero arithmetic effect on PI.</summary>
    public Guid? RelatedWeatherLogId { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public ManpowerLogEntryKind EntryKind { get; private set; }

    /// <summary>Required iff <see cref="EntryKind"/> is not <see cref="ManpowerLogEntryKind.Original"/>.</summary>
    public Guid? CorrectsLogId { get; private set; }

    /// <summary>Required iff <see cref="EntryKind"/> is not <see cref="ManpowerLogEntryKind.Original"/>
    /// (§4.7 rule 5 - no countersignature, but a reason is mandatory).</summary>
    public string? CorrectionReason { get; private set; }

    /// <summary>§4.4 Q8's ruling: warn-and-confirm, not a hard block. <see langword="true"/> when the
    /// caller explicitly overrode a 409 <c>ManpowerLogAlreadyExists</c> duplicate-key warning - kept
    /// on the row so a reviewer can see the override happened, not merely that the row exists.</summary>
    public bool AllowDuplicateOverride { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ManpowerEquipmentLog()
    {
    }

    private ManpowerEquipmentLog(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset logDate,
        Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        Guid? activityId,
        LabourType labourType,
        string? subcontractorRef,
        int workerCount,
        decimal manHours,
        decimal overtimeHours,
        bool manHoursDerived,
        int equipmentCount,
        decimal equipmentOperatingHours,
        decimal equipmentStandbyHours,
        string? workDescription,
        Guid? relatedWeatherLogId,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        ManpowerLogEntryKind entryKind,
        Guid? correctsLogId,
        string? correctionReason,
        bool allowDuplicateOverride)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("ManpowerEquipmentLog.ProjectId is required.");
        }

        if (workCategoryId == Guid.Empty)
        {
            throw new DomainException("ManpowerEquipmentLog.WorkCategoryId is required.");
        }

        if (recordedByUserId == Guid.Empty)
        {
            throw new DomainException("ManpowerEquipmentLog.RecordedByUserId is required.");
        }

        if (workerCount < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.WorkerCount cannot be negative.");
        }

        if (manHours < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.ManHours cannot be negative.");
        }

        if (overtimeHours < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.OvertimeHours cannot be negative.");
        }

        if (equipmentCount < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.EquipmentCount cannot be negative.");
        }

        if (equipmentOperatingHours < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.EquipmentOperatingHours cannot be negative.");
        }

        if (equipmentStandbyHours < 0)
        {
            throw new DomainException("ManpowerEquipmentLog.EquipmentStandbyHours cannot be negative.");
        }

        // §4.1 validation table, rule 1: catches the 6,000-for-600 typo; a 24h ceiling accommodates
        // any shift pattern.
        if (manHours > workerCount * 24.00m)
        {
            throw new DomainException("ManpowerEquipmentLog.ManHours cannot exceed WorkerCount * 24.00.");
        }

        // Rule 2: hours without people is incoherent.
        if (manHours > 0 && workerCount == 0)
        {
            throw new DomainException("ManpowerEquipmentLog.ManHours > 0 requires WorkerCount > 0.");
        }

        // Rule 3: OT is a subset, not an addition.
        if (overtimeHours > manHours)
        {
            throw new DomainException("ManpowerEquipmentLog.OvertimeHours cannot exceed ManHours.");
        }

        // Rule 4: same class of guard, for equipment.
        if (equipmentOperatingHours + equipmentStandbyHours > equipmentCount * 24.00m)
        {
            throw new DomainException(
                "ManpowerEquipmentLog.EquipmentOperatingHours + EquipmentStandbyHours cannot exceed EquipmentCount * 24.00.");
        }

        if (entryKind == ManpowerLogEntryKind.Original)
        {
            if (correctsLogId is not null)
            {
                throw new DomainException("An Original manpower log entry must not carry CorrectsLogId.");
            }

            if (correctionReason is not null)
            {
                throw new DomainException("An Original manpower log entry must not carry CorrectionReason.");
            }
        }
        else
        {
            if (correctsLogId is null || correctsLogId.Value == Guid.Empty)
            {
                throw new DomainException($"ManpowerEquipmentLog.CorrectsLogId is required for a {entryKind} entry.");
            }

            if (string.IsNullOrWhiteSpace(correctionReason))
            {
                throw new DomainException($"ManpowerEquipmentLog.CorrectionReason is required for a {entryKind} entry.");
            }
        }

        TenantId = tenantId;
        ProjectId = projectId;
        LogDate = logDate;
        Shift = shift;
        WorkCategoryId = workCategoryId;
        WbsNodeId = wbsNodeId;
        ActivityId = activityId;
        LabourType = labourType;
        SubcontractorRef = subcontractorRef;
        WorkerCount = workerCount;
        ManHours = manHours;
        OvertimeHours = overtimeHours;
        ManHoursDerived = manHoursDerived;
        EquipmentCount = equipmentCount;
        EquipmentOperatingHours = equipmentOperatingHours;
        EquipmentStandbyHours = equipmentStandbyHours;
        WorkDescription = workDescription;
        RelatedWeatherLogId = relatedWeatherLogId;
        RecordedByUserId = recordedByUserId;
        RecordedAt = recordedAt;
        EntryKind = entryKind;
        CorrectsLogId = correctsLogId;
        CorrectionReason = correctionReason;
        AllowDuplicateOverride = allowDuplicateOverride;
    }

    /// <summary>The only way to create a first-time entry - <see cref="EntryKind"/> is always
    /// <see cref="ManpowerLogEntryKind.Original"/>, <see cref="CorrectsLogId"/>/
    /// <see cref="CorrectionReason"/> are always null.</summary>
    public static ManpowerEquipmentLog CreateOriginal(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset logDate,
        Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        Guid? activityId,
        LabourType labourType,
        string? subcontractorRef,
        int workerCount,
        decimal manHours,
        decimal overtimeHours,
        bool manHoursDerived,
        int equipmentCount,
        decimal equipmentOperatingHours,
        decimal equipmentStandbyHours,
        string? workDescription,
        Guid? relatedWeatherLogId,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        bool allowDuplicateOverride) =>
        new(
            tenantId, projectId, logDate, shift, workCategoryId, wbsNodeId, activityId, labourType,
            subcontractorRef, workerCount, manHours, overtimeHours, manHoursDerived, equipmentCount,
            equipmentOperatingHours, equipmentStandbyHours, workDescription, relatedWeatherLogId,
            recordedByUserId, recordedAt, ManpowerLogEntryKind.Original, correctsLogId: null,
            correctionReason: null, allowDuplicateOverride);

    /// <summary>A replacement entry - "there was a log, but the true figures are these" (§4.7 rule
    /// 6: "it replaces; it does not patch" - the correction's own values, including
    /// <see cref="LogDate"/> and <see cref="WorkCategoryId"/>, govern completely).</summary>
    public static ManpowerEquipmentLog CreateCorrection(
        Guid tenantId,
        Guid projectId,
        Guid correctsLogId,
        string correctionReason,
        DateTimeOffset logDate,
        Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        Guid? activityId,
        LabourType labourType,
        string? subcontractorRef,
        int workerCount,
        decimal manHours,
        decimal overtimeHours,
        bool manHoursDerived,
        int equipmentCount,
        decimal equipmentOperatingHours,
        decimal equipmentStandbyHours,
        string? workDescription,
        Guid? relatedWeatherLogId,
        Guid recordedByUserId,
        DateTimeOffset recordedAt) =>
        new(
            tenantId, projectId, logDate, shift, workCategoryId, wbsNodeId, activityId, labourType,
            subcontractorRef, workerCount, manHours, overtimeHours, manHoursDerived, equipmentCount,
            equipmentOperatingHours, equipmentStandbyHours, workDescription, relatedWeatherLogId,
            recordedByUserId, recordedAt, ManpowerLogEntryKind.Correction, correctsLogId, correctionReason,
            allowDuplicateOverride: false);

    /// <summary>Voids the target entirely - removes both itself and its target from the effective
    /// log set (§4.7). Carries the same full field set as <see cref="CreateCorrection"/> (the schema
    /// draws no distinction) even though its content is moot once retracted - recorded for the audit
    /// trail only.</summary>
    public static ManpowerEquipmentLog CreateRetraction(
        Guid tenantId,
        Guid projectId,
        Guid correctsLogId,
        string correctionReason,
        DateTimeOffset logDate,
        Shift shift,
        Guid workCategoryId,
        Guid? wbsNodeId,
        Guid? activityId,
        LabourType labourType,
        string? subcontractorRef,
        int workerCount,
        decimal manHours,
        decimal overtimeHours,
        bool manHoursDerived,
        int equipmentCount,
        decimal equipmentOperatingHours,
        decimal equipmentStandbyHours,
        string? workDescription,
        Guid? relatedWeatherLogId,
        Guid recordedByUserId,
        DateTimeOffset recordedAt) =>
        new(
            tenantId, projectId, logDate, shift, workCategoryId, wbsNodeId, activityId, labourType,
            subcontractorRef, workerCount, manHours, overtimeHours, manHoursDerived, equipmentCount,
            equipmentOperatingHours, equipmentStandbyHours, workDescription, relatedWeatherLogId,
            recordedByUserId, recordedAt, ManpowerLogEntryKind.Retraction, correctsLogId, correctionReason,
            allowDuplicateOverride: false);
}
