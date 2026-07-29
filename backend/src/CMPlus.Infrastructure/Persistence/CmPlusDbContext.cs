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
