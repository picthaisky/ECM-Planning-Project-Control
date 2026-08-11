using System.Reflection;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// EF Core Code-First DbContext for CM+ Project Control (S1-BE-03). Applies a global tenant query
/// filter to every entity implementing <see cref="ITenantOwned"/> (ADR-0002) and stamps
/// <c>TenantId</c> server-side from <see cref="ITenantProvider"/> on every insert - a TenantId
/// carried by an entity from any other source (including a value that originated in a client
/// payload) is overwritten here and never trusted, per docs/db-conventions.md §2 rule 4.
/// </summary>
public sealed class CmPlusDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public CmPlusDbContext(DbContextOptions<CmPlusDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    // Tenant is explicitly exempt from tenant scoping (docs/db-conventions.md §2 rule 5) - it is
    // the multi-tenancy root, not tenant-owned data.
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<WBSNode> WBSNodes => Set<WBSNode>();

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<ActivityRelation> ActivityRelations => Set<ActivityRelation>();

    public DbSet<Calendar> Calendars => Set<Calendar>();

    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();

    public DbSet<ActivityProgressLog> ActivityProgressLogs => Set<ActivityProgressLog>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<ApprovalPolicyRule> ApprovalPolicyRules => Set<ApprovalPolicyRule>();

    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();

    public DbSet<FileImportJob> FileImportJobs => Set<FileImportJob>();

    /// <summary>S7-BE-05: append-only, immutable (ADR-0009) - no update/delete path exists anywhere
    /// (entity has no mutator methods/setters; see <see cref="Configurations.EvmPeriodSnapshotConfiguration"/>).</summary>
    public DbSet<EvmPeriodSnapshot> EvmPeriodSnapshots => Set<EvmPeriodSnapshot>();

    /// <summary>Actual Cost (AC/ACWP) ledger - append-only (actual-cost.md §6.5/§9, ADR-0013); no
    /// update/delete path exists anywhere (entity has no mutator methods/setters; see
    /// <see cref="Configurations.ActualCostEntryConfiguration"/>).</summary>
    public DbSet<ActualCostEntry> ActualCostEntries => Set<ActualCostEntry>();

    /// <summary>Payment Certificate (IPC) aggregate - S9-BE-01, approval-workflow.md §3/§4. Carries
    /// a SQL Server <c>rowversion</c> optimistic-concurrency token (see
    /// <see cref="Configurations.PaymentCertificateConfiguration"/>).</summary>
    public DbSet<PaymentCertificate> PaymentCertificates => Set<PaymentCertificate>();

    /// <summary>The resolved approval-chain snapshot owned by each <see cref="PaymentCertificate"/> -
    /// security review sprint-09.md H-01 fix. Not append-only: <c>ReturnForRevision</c>/<c>Withdraw</c>
    /// legitimately clear and re-populate it (see <see cref="Entities.PaymentCertificateApprovalStep"/>'s
    /// own remarks).</summary>
    public DbSet<PaymentCertificateApprovalStep> PaymentCertificateApprovalSteps => Set<PaymentCertificateApprovalStep>();

    /// <summary>Retention/advance ledger - append-only (payment-retention.md §4, S9-BE-04); no
    /// update/delete path exists anywhere (entity has no mutator methods/setters; see
    /// <see cref="Configurations.ProjectFinanceLedgerConfiguration"/>). $R^{cum}$/$D^{cum}$ are
    /// always read via <c>IProjectFinanceLedgerReader</c>'s <c>SUM()</c>, never recomputed.</summary>
    public DbSet<ProjectFinanceLedger> ProjectFinanceLedgers => Set<ProjectFinanceLedger>();

    /// <summary>Variation Order (VO/CO) aggregate - S10-BE-01, domain-rules.md §2. Carries a SQL
    /// Server <c>rowversion</c> optimistic-concurrency token (see
    /// <see cref="Configurations.VariationOrderConfiguration"/>). A VO approval never writes a
    /// <see cref="ProjectFinanceLedger"/> row (domain-rules.md §6.4) - by construction, there is no
    /// code path anywhere in this type that could build one.</summary>
    public DbSet<VariationOrder> VariationOrders => Set<VariationOrder>();

    /// <summary>The resolved approval-chain snapshot owned by each <see cref="VariationOrder"/> -
    /// same H-01 fix shape as <see cref="PaymentCertificateApprovalSteps"/>, but with a UNIQUE
    /// natural-key index from day one (domain-rules.md §2.4: "do not repeat" the still-open IPC
    /// N-04 gap).</summary>
    public DbSet<VariationOrderApprovalStep> VariationOrderApprovalSteps => Set<VariationOrderApprovalStep>();

    /// <summary>The scope payload - <c>{ActivityId, BudgetCostDelta}</c> delta lines whose sum always
    /// equals the owning <see cref="VariationOrder"/>'s <c>Amount</c> exactly (domain-rules.md §5.2).</summary>
    public DbSet<VariationOrderScopeItem> VariationOrderScopeItems => Set<VariationOrderScopeItem>();

    /// <summary>S11-BE-01 (US-11.1): immutable weather log, legal evidence for EOT claims - append-only
    /// (no update/delete path exists anywhere; entity has no mutator methods/setters; see
    /// <see cref="Configurations.DailyWeatherLogConfiguration"/>). Structurally enforced via the
    /// <see cref="Domain.Common.IAppendOnly"/> marker (security review sprint-09.md M-01 pattern).</summary>
    public DbSet<DailyWeatherLog> DailyWeatherLogs => Set<DailyWeatherLog>();

    /// <summary>The affected-activity tags owned by each <see cref="DailyWeatherLog"/> (docs/9 §4's
    /// <c>AffectedActivityIds</c>) - data capture only, see that type's own remarks. Also append-only.</summary>
    public DbSet<DailyWeatherLogActivity> DailyWeatherLogActivities => Set<DailyWeatherLogActivity>();

    /// <summary>S11-BE-03 (US-11.2): site issue / action-log entries, <c>Open</c>-&gt;<c>Doing</c>-&gt;
    /// <c>Closed</c>. Ordinary mutable aggregate, unlike <see cref="DailyWeatherLog"/> above.</summary>
    public DbSet<IssueLog> IssueLogs => Set<IssueLog>();

    /// <summary>ADR-0019 (domain-rules.md weather-eot §4.3): append-only CPM run history - captured
    /// by <c>RecalculateCpmCommandHandler</c> every time CPM recalculates, alongside (never instead
    /// of) the live <see cref="Activity.IsCritical"/>/<see cref="Activity.TotalFloat"/>/
    /// <see cref="Activity.FreeFloat"/> write-back. Structurally enforced via the
    /// <see cref="Domain.Common.IAppendOnly"/> marker (security review sprint-09.md M-01 pattern) -
    /// see <see cref="Configurations.CpmRunConfiguration"/>. Prerequisite for S11-BE-02's
    /// EotEvaluator, which needs the schedule as it stood on a stoppage date, not as it stands today.</summary>
    public DbSet<CpmRun> CpmRuns => Set<CpmRun>();

    /// <summary>The run's per-activity results (domain-rules.md §4.3) - owned by each
    /// <see cref="CpmRun"/>. Also append-only.</summary>
    public DbSet<CpmRunActivity> CpmRunActivities => Set<CpmRunActivity>();

    /// <summary>The run's network topology (domain-rules.md §4.3 - "not optional": the evaluator
    /// re-runs CpmEngine against this, not just the per-activity floats). Also append-only.</summary>
    public DbSet<CpmRunRelation> CpmRunRelations => Set<CpmRunRelation>();

    /// <summary>S11-BE-02 (domain-rules.md weather-eot §3.5): per-project EOT policy configuration.
    /// A missing row is not a configuration error - <c>EvaluateEotCommandHandler</c> substitutes
    /// <c>EotPolicySettings.Default</c> when this reads <see langword="null"/>.</summary>
    public DbSet<ProjectEotPolicy> ProjectEotPolicies => Set<ProjectEotPolicy>();

    /// <summary>S11-BE-02: one immutable, dated, reproducible <c>EotEvaluator</c> snapshot
    /// (domain-rules.md weather-eot §8.5) - append-only, structurally enforced via
    /// <see cref="Domain.Common.IAppendOnly"/> exactly like <see cref="CpmRuns"/>/
    /// <see cref="DailyWeatherLogs"/>. "A correction never mutates a stored evaluation" (§8.5 rule 1) -
    /// the remedy for a stale evaluation is always a brand new row.</summary>
    public DbSet<EotEvaluation> EotEvaluations => Set<EotEvaluation>();

    /// <summary>One governing <see cref="CpmRun"/>'s contribution to an <see cref="EotEvaluation"/>
    /// (domain-rules.md §5.1) - owned by each evaluation. Also append-only.</summary>
    public DbSet<EotEvaluationRun> EotEvaluationRuns => Set<EotEvaluationRun>();

    /// <summary>One considered <see cref="DailyWeatherLog"/> entry's computed fate within an
    /// <see cref="EotEvaluation"/> (domain-rules.md §3: "the evaluator never silently drops a row").
    /// Also append-only.</summary>
    public DbSet<EotEvaluationSource> EotEvaluationSources => Set<EotEvaluationSource>();

    /// <summary>domain-rules.md §5.4's explainability row - one per (governing run, activity) pair
    /// charged at least one countable day within an <see cref="EotEvaluation"/>. Also append-only.</summary>
    public DbSet<EotEvaluationDriver> EotEvaluationDrivers => Set<EotEvaluationDriver>();

    /// <summary>S12-BE-01 (US-12.1): site progress photos - metadata + <c>StorageKey</c> only, never
    /// the file bytes themselves (those live behind <see cref="Application.Abstractions.IFileStorage"/>,
    /// see <see cref="Configurations.PhotoConfiguration"/>). Ordinary mutable aggregate today (not
    /// <see cref="Domain.Common.IAppendOnly"/>) since this task's scope is upload + serve only - no
    /// update/delete path exists yet either way.</summary>
    public DbSet<Photo> Photos => Set<Photo>();

    /// <summary>S12-BE-02 (US-12.2): หมวดงาน taxonomy - domain-rules.md (manpower-equipment) §4.3.
    /// <see cref="WorkCategory.ProjectId"/> nullable = tenant-wide default catalogue entry.</summary>
    public DbSet<WorkCategory> WorkCategories => Set<WorkCategory>();

    /// <summary>S12-BE-02: append-only daily manpower/equipment site log - the source of "actual
    /// man-hours" (AMH) behind the Productivity Index. Structurally enforced via
    /// <see cref="Domain.Common.IAppendOnly"/> (security review sprint-09.md M-01 pattern) - see
    /// <see cref="Configurations.ManpowerEquipmentLogConfiguration"/> and the entity's own remarks.</summary>
    public DbSet<ManpowerEquipmentLog> ManpowerEquipmentLogs => Set<ManpowerEquipmentLog>();

    /// <summary>S12-BE-02: editable, audited manning plan - domain-rules.md §4.6(a)'s rejection of
    /// putting a mutable <c>PlannedManCount</c> on the immutable <see cref="ManpowerEquipmentLog"/>
    /// row. Ordinary mutable aggregate, unlike its sibling above.</summary>
    public DbSet<ManpowerPlan> ManpowerPlans => Set<ManpowerPlan>();

    /// <summary>S13-BE-01/S13-DB-01 (ADR-0005 US-13.1, closes security review sprint-12.md M-01):
    /// server-side <c>Idempotency-Key</c> dedupe record for a site-module write endpoint. Ordinary
    /// mutable aggregate (NOT <see cref="Domain.Common.IAppendOnly"/>) - see
    /// <see cref="Domain.Entities.IdempotencyKey"/>'s own remarks for why.</summary>
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    /// <summary>S14-BE-01 (US-14.1): a saved schedule/budget reference point. Ordinary mutable
    /// aggregate at the parent level (<see cref="Domain.Entities.Baseline.IsActive"/> must stay
    /// editable for the project's whole life) - see that entity's own remarks for why it is
    /// deliberately NOT <see cref="Domain.Common.IAppendOnly"/> even though its children are.
    /// </summary>
    public DbSet<Baseline> Baselines => Set<Baseline>();

    /// <summary>The frozen per-activity snapshot rows owned by each <see cref="Baseline"/> -
    /// "Baseline snapshots are historical evidence" (S14-BE-01 brief). Structurally enforced via
    /// <see cref="Domain.Common.IAppendOnly"/> (security review sprint-09.md M-01 pattern) - see
    /// <see cref="Configurations.BaselineActivitySnapshotConfiguration"/> and the entity's own
    /// remarks.</summary>
    public DbSet<BaselineActivitySnapshot> BaselineActivitySnapshots => Set<BaselineActivitySnapshot>();

    /// <summary>
    /// Narrow, explicitly grep-able escape hatch for the S3-BE-04 bulk-import path (ADR-0002-style
    /// discipline: any bypass of a default cross-cutting behaviour must be visible, not casual).
    /// <see cref="Interceptors.AuditSaveChangesInterceptor"/>'s default is one <c>AuditLog</c> row
    /// per changed entity, which is correct for ordinary commands but would turn a 10,000-activity/
    /// 15,000-relation schedule import into 25,000+ audit rows - directly contradicting
    /// docs/db-conventions.md §8 ("one summarizing AuditLog entry" per bulk operation). Set to
    /// <see langword="true"/> only for the duration of a bulk-import <c>SaveChanges</c> call, whose
    /// caller (<c>ImportRepository</c>) adds its own single summarizing <see cref="AuditLog"/> row
    /// directly - never left on for an ordinary command's <c>SaveChanges</c>.
    /// </summary>
    public bool SuppressPerEntityAudit { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CmPlusDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    // S11-DB-01 (database-engineer, ADR-0019 finding on IX_CpmRunActivities_CpmRunId/
    // IX_CpmRunRelations_CpmRunId): investigated removing EF's auto-generated single-column FK index
    // here (it is genuinely redundant given the explicit (TenantId, CpmRunId) index next to it - see
    // the S11-DB-01 report for the ToQueryString() evidence). `IMutableEntityType.RemoveIndex(...)`
    // was tried and empirically does NOT stick: EF's `ForeignKeyIndexConvention` reactively re-adds a
    // covering index the moment the auto one is removed, within the same OnModelCreating pass
    // (confirmed by instrumenting this method and inspecting `entityType.GetIndexes()` immediately
    // after the RemoveIndex call - the bare index was still present). This is not unique to CpmRun:
    // the identical bare-FK-index-plus-tenant-leading-composite pattern exists throughout this schema
    // back to Sprint 1 (e.g. IX_WBSNodes_ParentWbsNodeId, IX_ActivityProgressLogs_ActivityId,
    // IX_CalendarExceptions_CalendarId, IX_DailyWeatherLogActivities_ActivityId,
    // IX_VariationOrderScopeItems_VariationOrderId, IX_EotEvaluationDrivers_EotEvaluationId) - a
    // systemic consequence of db-conventions.md §2 rule 2 ("TenantId leads every composite index, no
    // exceptions") colliding with EF's FK-index convention, not a Sprint 11-specific oversight. A
    // real fix needs either a custom convention replacing/wrapping ForeignKeyIndexConvention (global
    // blast radius across every FK'd table in the schema - a system-architect/ADR-level decision, not
    // a local patch) or accepting the bounded cost as-is. Deliberately NOT attempted piecemeal here -
    // see the S11-DB-01 report for the recommendation.

    /// <summary>
    /// Applies <c>HasQueryFilter(e => e.TenantId == _tenantProvider.TenantId)</c> to every entity
    /// implementing <see cref="ITenantOwned"/>, discovered via reflection over the built model
    /// (ADR-0002). A new tenant-owned entity gets the filter automatically - no per-entity wiring
    /// to remember and no way to forget it.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var method = typeof(CmPlusDbContext)
                .GetMethod(nameof(SetTenantQueryFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            // Instance method invoked on `this` (not a static helper taking the context as a
            // parameter) so the filter lambda closes over `this._tenantProvider` directly - the
            // pattern EF Core's query-filter/parameter re-evaluation is built around. A static
            // method taking the DbContext as an explicit parameter produced queries that failed to
            // translate when combined with a second predicate (verified empirically in
            // Integration.Tests against Sqlite).
            method.Invoke(this, [modelBuilder]);
        }
    }

    private void SetTenantQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantProvider.TenantId);
    }

    public override int SaveChanges()
    {
        StampTenantId();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Server-side TenantId stamping (ADR-0002, non-negotiable): every newly-added
    /// <see cref="ITenantOwned"/> entity gets <c>TenantId</c> forced to the current
    /// <see cref="ITenantProvider"/> value, regardless of whatever value the entity held
    /// beforehand.
    /// </summary>
    private void StampTenantId()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity is ITenantOwned)
            {
                entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = _tenantProvider.TenantId;
            }
        }
    }
}
