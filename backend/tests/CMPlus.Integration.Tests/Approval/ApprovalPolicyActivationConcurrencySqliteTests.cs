using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Configurations;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Integration.Tests.ApprovalOrdering;

/// <summary>
/// S14-BE-01 review follow-up, closed by ADR-0021's index redesign (2026-08-11 - Sprint 15
/// approval-policy hardening). History kept below because it is exactly what proves the fix is real:
/// this file first documented the defect on a real, constraint-enforcing engine, then the index was
/// redesigned, then the same tests re-run here prove the corruption is gone.
///
/// <para><b>Original finding (still true for the group it describes):</b>
/// <see cref="Configurations.ApprovalPolicyConfiguration"/> used to ship a single filtered unique
/// index `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1`, the same shape
/// <see cref="Domain.Entities.Baseline"/> uses successfully. Standard SQL unique-index semantics
/// (ANSI, not a SQLite quirk - the same rule SQL Server follows) treat NULL as never equal to another
/// NULL for uniqueness purposes, so that single index provided real protection whenever
/// <c>ProjectId</c> was a genuine, non-null value
/// (<see cref="Inserting_A_Second_Active_Policy_Version_With_A_Real_ProjectId_Directly_Violates_The_Unique_Index"/>,
/// <see cref="SaveChangesAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race"/>)
/// but <b>zero</b> protection whenever both competing rows had <c>ProjectId = null</c> - which is
/// every row <c>UpdateApprovalPolicyCommandHandler</c> can actually create (tenant-wide default;
/// project-scoped override is schema-present but unexposed - see
/// <see cref="ApprovalPolicy.ProjectId"/>'s own remarks). Two concurrent requests could both commit,
/// leaving two simultaneously-active policy versions for the same tenant/document type permanently -
/// not merely one request receiving a reportable conflict, but silent, persistent data-integrity
/// corruption no exception-handling fix in <see cref="ApprovalPolicyRepository"/> could ever catch,
/// because no exception was ever thrown for this case.</para>
///
/// <para><b>The fix (this file's current state proves it, does not merely assert it):</b>
/// <see cref="Configurations.ApprovalPolicyConfiguration"/> now ships <b>two</b> filtered unique
/// indexes on disjoint <c>ProjectId</c>-nullability groups - `(TenantId, DocumentType) WHERE
/// IsActive = 1 AND ProjectId IS NULL` (closes the live defect: every key column is now non-null
/// wherever the filter applies, so ANSI uniqueness actually bites) and `(TenantId, ProjectId,
/// DocumentType) WHERE IsActive = 1 AND ProjectId IS NOT NULL` (the original ADR-0008 guarantee,
/// narrowed so the two indexes are provably disjoint - two policies with different
/// nullability can never collide with each other, by construction). See
/// <see cref="Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version"/>
/// for the green proof and
/// <see cref="Mutation_Proof_The_Old_Single_Index_Would_Not_Have_Caught_This_The_New_Split_Index_Does"/>
/// for the red-first companion (same scenario, run against a locally-reproduced pre-fix schema, in
/// the same test run) - the mutation-testing discipline this codebase requires (see
/// <c>BaselineActivationOrderingSqliteTests</c>' own mutation checks) applied to an index-shape
/// change rather than a code branch.</para>
///
/// <para>Sqlite, not SQL Server, for the same reason <c>BaselineActivationOrderingSqliteTests</c> uses
/// it: Docker cannot start in this environment. The NULL-uniqueness behaviour this file documents is
/// standard ANSI SQL, not a SQLite-specific quirk, and SQL Server's own filtered-index CREATE-time
/// validation (a CREATE UNIQUE INDEX against data that already violates it fails, per
/// <c>artifacts/migrations/20260811_sprint15_approvalpolicy_split_singleactive_index.PREFLIGHT.sql</c>)
/// is standard documented SQL Server behaviour - but neither claim about SQL Server specifically is
/// executed here, only inferred from ANSI semantics both engines implement; see this task's report
/// for what remains unproven without a real SQL Server run.</para>
/// </summary>
public class ApprovalPolicyActivationConcurrencySqliteTests
{
    /// <summary>Schema-only DbContext used exclusively for <c>EnsureCreatedAsync</c>, mapping only
    /// <see cref="ApprovalPolicy"/>/<see cref="ApprovalPolicyRule"/> via the real, unmodified
    /// production configuration classes - mirrors <c>BaselineActivationOrderingSqliteTests</c>'s
    /// identical <c>BaselineSchemaOnlyDbContext</c> split, for the identical reason
    /// (<c>CmPlusDbContext.OnModelCreating</c> maps the whole schema, and the unrelated
    /// <c>AuditLogConfiguration</c>'s <c>nvarchar(max)</c> column does not parse under Sqlite's column
    /// grammar).</summary>
    private sealed class ApprovalPolicySchemaOnlyDbContext(DbContextOptions<ApprovalPolicySchemaOnlyDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new ApprovalPolicyConfiguration());
            modelBuilder.ApplyConfiguration(new ApprovalPolicyRuleConfiguration());
        }
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApprovalPolicySchemaOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var schemaContext = new ApprovalPolicySchemaOnlyDbContext(options);
        await schemaContext.Database.EnsureCreatedAsync();
    }

    private static CmPlusDbContext CreateContext(SqliteConnection connection, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseSqlite(connection)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> Rules =
    [
        new(1, 0.00m, null, UserRole.QS),
    ];

    /// <summary>
    /// <b>Mutation check (same discipline as
    /// <c>BaselineActivationOrderingSqliteTests.TryActivateAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race</c>):</b>
    /// with <see cref="ApprovalPolicyRepository.SaveChangesAsync"/>'s
    /// <c>catch (DbUpdateException ex) when (UniqueIndexViolationClassifier.IsUniqueConstraintViolation(ex))</c>
    /// branch removed, this test fails - the raw <see cref="DbUpdateException"/> from request B's save
    /// escapes unhandled straight out of the <c>await repoB.SaveChangesAsync()</c> call below
    /// (deliberately not wrapped in a local try/catch, for the same reason the Baseline test isn't).
    /// With the fix in place, the same call cleanly returns <see langword="false"/>, and exactly one
    /// policy version ends up active.
    ///
    /// <para><b>Deliberately uses a non-null <c>ProjectId</c></b> - not the <c>projectId: null</c>
    /// shape <c>UpdateApprovalPolicyCommandHandler</c> actually calls today. At the time this test was
    /// first written, a null <c>ProjectId</c> made the (then-single) unique index never fire at all -
    /// see
    /// <see cref="Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version"/>
    /// and its mutation-proof companion for that history and the now-fixed current behaviour - so that
    /// shape could not exercise (or prove) the exception-classification fix this test is actually
    /// about. This test exercises <see cref="ApprovalPolicyRepository"/> directly against a real
    /// project-scoped policy pair to prove the classifier logic itself is correct - the identical code
    /// path a project-scoped override hits once/if that write surface is exposed in a later sprint.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        Guid v1Id;
        await using (var seedContext = CreateContext(connection, tenantProvider))
        {
            var v1 = ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId, ApprovalDocumentType.PaymentCertificate, now.AddYears(-1), Rules);
            seedContext.ApprovalPolicies.Add(v1);
            await seedContext.SaveChangesAsync();
            v1Id = v1.Id;
        }

        // Two independent DbContexts/repositories - request A and request B - each loading the SAME
        // pre-race active policy (v1) before either one calls SaveChangesAsync, mirroring
        // UpdateApprovalPolicyCommandHandler.Handle's own read-then-write shape run twice at once.
        // Deliberately NOT via IApprovalPolicyRepository.FindActiveTenantDefaultAsync - that method's
        // own query is hardcoded to `p.ProjectId == null` (the only case the shipped write surface
        // actually supports today), so it cannot find this test's deliberately project-scoped v1 at
        // all. Loaded directly (still tracked, same downstream effect) purely to exercise
        // ApprovalPolicyRepository.SaveChangesAsync's classifier logic against a real non-null-ProjectId
        // collision - see this method's own remarks for why that's the only shape that can prove it.
        await using var contextA = CreateContext(connection, tenantProvider);
        var repoA = new ApprovalPolicyRepository(contextA);
        var currentA = await contextA.ApprovalPolicies.FirstOrDefaultAsync(p => p.Id == v1Id);
        Assert.NotNull(currentA);
        var nextVersionA = currentA!.CreateNextVersion(now, Rules, allowSelfApproval: true, null, null);
        currentA.Deactivate(now);
        repoA.AddVersion(nextVersionA);

        await using var contextB = CreateContext(connection, tenantProvider);
        var repoB = new ApprovalPolicyRepository(contextB);
        var currentB = await contextB.ApprovalPolicies.FirstOrDefaultAsync(p => p.Id == v1Id);
        Assert.NotNull(currentB);
        var nextVersionB = currentB!.CreateNextVersion(now, Rules, allowSelfApproval: false, null, null);
        currentB.Deactivate(now);
        repoB.AddVersion(nextVersionB);

        // Request A wins the race, committing first.
        var winnerSucceeded = await repoA.SaveChangesAsync();
        Assert.True(winnerSucceeded);

        // Request B is the loser - deliberately not wrapped in try/catch (see this method's remarks).
        var loserSucceeded = await repoB.SaveChangesAsync();
        Assert.False(loserSucceeded);

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.ApprovalPolicies
            .CountAsync(p => p.ProjectId == projectId && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive);
        Assert.Equal(1, activeCount);

        var activeId = await verifyContext.ApprovalPolicies
            .Where(p => p.ProjectId == projectId && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive)
            .Select(p => p.Id)
            .SingleAsync();
        Assert.Equal(nextVersionA.Id, activeId);
    }

    /// <summary>Confirms the project-scoped filtered unique index (`... WHERE IsActive = 1 AND
    /// ProjectId IS NOT NULL`) actually materializes on Sqlite for the real
    /// <see cref="ApprovalPolicyConfiguration"/> and genuinely rejects a second active row when
    /// <c>ProjectId</c> is a real value - the direct check that the constraint the test above relies
    /// on is really there (mirrors <c>BaselineActivationOrderingSqliteTests</c>'s identical
    /// direct-violation check). Contrast with
    /// <see cref="Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version"/>
    /// immediately below - the only difference between the two tests is a null vs. non-null
    /// <c>ProjectId</c>, which used to be the difference between "rejected" and "silently accepted"
    /// and is now "rejected" in both cases, each by its own one of the two split indexes.</summary>
    [Fact]
    public async Task Inserting_A_Second_Active_Policy_Version_With_A_Real_ProjectId_Directly_Violates_The_Unique_Index()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        await using (var context = CreateContext(connection, tenantProvider))
        {
            var v1 = ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId, ApprovalDocumentType.PaymentCertificate, now.AddYears(-1), Rules);
            context.ApprovalPolicies.Add(v1);
            await context.SaveChangesAsync();
        }

        await using var secondContext = CreateContext(connection, tenantProvider);
        var v2 = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId, ApprovalDocumentType.PaymentCertificate, now, Rules);
        secondContext.ApprovalPolicies.Add(v2);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    /// <summary>
    /// <b>The defect ADR-0021 recorded, now closed - this is the green half of the red/green pair
    /// with <see cref="Mutation_Proof_The_Old_Single_Index_Would_Not_Have_Caught_This_The_New_Split_Index_Does"/>.</b>
    /// Every policy <c>UpdateApprovalPolicyCommandHandler</c> can actually create today passes
    /// <c>projectId: null</c> (tenant-wide default - the only currently-exposed write surface; see
    /// <see cref="ApprovalPolicy.ProjectId"/>'s own remarks). Under the OLD single index this scenario
    /// silently succeeded twice (see the mutation-proof test for the executed reproduction of that).
    /// Under the CURRENT, real <see cref="ApprovalPolicyConfiguration"/> - two filtered indexes split
    /// on <c>ProjectId</c> nullability - the second concurrent "activation" now collides with
    /// `IX_ApprovalPolicies_TenantId_DocumentType WHERE IsActive = 1 AND ProjectId IS NULL`, whose key
    /// columns (<c>TenantId</c>, <c>DocumentType</c>) are both non-null wherever the filter applies, so
    /// ANSI uniqueness bites exactly the way it always did for <see cref="Domain.Entities.Baseline"/>'s
    /// non-nullable-discriminator index. This test proves it directly against the real production
    /// configuration class, not a hypothetical.
    /// </summary>
    [Fact]
    public async Task Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        Guid v1Id;
        await using (var context = CreateContext(connection, tenantProvider))
        {
            var v1 = ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now.AddYears(-1), Rules);
            context.ApprovalPolicies.Add(v1);
            await context.SaveChangesAsync();
            v1Id = v1.Id;
        }

        // A second, wholly independent tenant-wide-default policy, also IsActive=true, also
        // ProjectId=null - the exact scenario that used to commit silently (see the mutation-proof
        // test below for the executed old-index comparison). Deliberately not wrapped in try/catch:
        // an escaping DbUpdateException here is the correct, expected outcome for this test, not a
        // failure of it.
        await using var secondContext = CreateContext(connection, tenantProvider);
        var v2 = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now, Rules);
        secondContext.ApprovalPolicies.Add(v2);

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.ApprovalPolicies
            .CountAsync(p => p.ProjectId == null && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive);

        // The invariant now holds: exactly ONE row active for (TenantId, ProjectId=null,
        // DocumentType) - v1, since v2's insert never committed (its failed SaveChangesAsync rolled
        // back the whole implicit transaction for that context).
        Assert.Equal(1, activeCount);

        var activeId = await verifyContext.ApprovalPolicies
            .Where(p => p.ProjectId == null && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive)
            .Select(p => p.Id)
            .SingleAsync();
        Assert.Equal(v1Id, activeId);
    }

    /// <summary>Historical, test-only replica of the pre-ADR-0021-fix
    /// <see cref="ApprovalPolicyConfiguration"/> - the single filtered unique index `(TenantId,
    /// ProjectId, DocumentType) WHERE IsActive = 1` this migration replaced. Exists <b>only</b> to give
    /// <see cref="Mutation_Proof_The_Old_Single_Index_Would_Not_Have_Caught_This_The_New_Split_Index_Does"/>
    /// an executable red state to compare against in the same test run - never apply this shape to a
    /// real database again. Field-for-field identical to the real configuration except for the index
    /// definition itself, so the only variable between the red and green halves of that test is
    /// exactly the thing under test.</summary>
    private sealed class PreAdr0021ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
    {
        public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
        {
            builder.ToTable("ApprovalPolicies");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();

            builder.Property(p => p.TenantId).IsRequired();
            builder.Property(p => p.CumulativeVoEscalationPct).HasPrecision(5, 2);

            builder.HasMany(p => p.Rules)
                .WithOne()
                .HasForeignKey(r => r.ApprovalPolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Metadata.FindNavigation(nameof(ApprovalPolicy.Rules))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            // The pre-ADR-0021 shape: a single filtered unique index whose key includes a nullable
            // ProjectId. ANSI NULL<>NULL semantics mean this never fires for the ProjectId IS NULL
            // group - reproduced here deliberately, as the red state this task's fix replaced.
            builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.DocumentType })
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            builder.ToTable(tb => tb.HasCheckConstraint(
                "CK_ApprovalPolicies_CumulativeVoEscalationPct", "[CumulativeVoEscalationPct] IS NULL OR [CumulativeVoEscalationPct] BETWEEN 0 AND 100"));
        }
    }

    private sealed class PreAdr0021SchemaOnlyDbContext(DbContextOptions<PreAdr0021SchemaOnlyDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new PreAdr0021ApprovalPolicyConfiguration());
            modelBuilder.ApplyConfiguration(new ApprovalPolicyRuleConfiguration());
        }
    }

    /// <summary>
    /// <b>Mutation discipline (this task's brief: "a fix without the red-first proof is a guess"),
    /// applied to an index-shape change rather than a code branch.</b> Runs the <em>identical</em>
    /// scenario as
    /// <see cref="Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version"/>
    /// - two independent tenant-wide-default (<c>ProjectId = null</c>) policies for the same
    /// <c>(TenantId, DocumentType)</c>, both <c>IsActive = true</c> - but against
    /// <see cref="PreAdr0021ApprovalPolicyConfiguration"/>, a literal reproduction of the single index
    /// this migration replaced. <b>Red:</b> against the old shape, the second
    /// <c>SaveChangesAsync</c> succeeds with no exception at all, and the database is left holding two
    /// simultaneously-active rows - the exact corruption ADR-0021 recorded. <b>Green</b> is the sibling
    /// test above, which runs the same scenario against the real, current
    /// <see cref="ApprovalPolicyConfiguration"/> and gets a clean rejection instead. If someone reverts
    /// the production configuration back to this old shape, the sibling green test starts failing
    /// (asserting a throw that no longer happens) - that is the mutation coverage this pair provides,
    /// executed rather than merely narrated in a comment.
    /// </summary>
    [Fact]
    public async Task Mutation_Proof_The_Old_Single_Index_Would_Not_Have_Caught_This_The_New_Split_Index_Does()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var schemaOptions = new DbContextOptionsBuilder<PreAdr0021SchemaOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var schemaContext = new PreAdr0021SchemaOnlyDbContext(schemaOptions))
        {
            await schemaContext.Database.EnsureCreatedAsync();
        }

        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        await using (var context = CreateContext(connection, tenantProvider))
        {
            var v1 = ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now.AddYears(-1), Rules);
            context.ApprovalPolicies.Add(v1);
            await context.SaveChangesAsync();
        }

        // Same scenario as the green test above, against the old-shape schema this time. No
        // exception - the point of this test. Asserting the absence of a throw is deliberate, not an
        // oversight: SaveChangesAsync must complete successfully for the red-state reproduction to be
        // real, matching ADR-0021's own recorded finding.
        await using var secondContext = CreateContext(connection, tenantProvider);
        var v2 = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now, Rules);
        secondContext.ApprovalPolicies.Add(v2);
        await secondContext.SaveChangesAsync();

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.ApprovalPolicies
            .CountAsync(p => p.ProjectId == null && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive);

        Assert.Equal(2, activeCount);
    }

    /// <summary>
    /// Answers this task's explicit question - "does the deactivate-then-insert ordering interact
    /// with the new index the way it did for Baseline (the S14 ordering bug)?" - for the case that
    /// matters most: a single, non-racing, sequential update of the tenant-wide default
    /// (<c>ProjectId = null</c>) policy, mirroring <c>UpdateApprovalPolicyCommandHandler.Handle</c>'s
    /// exact shape verbatim (<see cref="ApprovalPolicyRepository.FindActiveTenantDefaultAsync"/> →
    /// <see cref="ApprovalPolicy.Deactivate"/> on the loaded row + <see cref="ApprovalPolicy.CreateNextVersion"/>
    /// staged via <see cref="ApprovalPolicyRepository.AddVersion"/> → one
    /// <see cref="ApprovalPolicyRepository.SaveChangesAsync"/> call flushing both in the same batch).
    ///
    /// <para><b>Finding: no interaction, unlike Baseline's.</b> ADR-0021 already records why:
    /// <c>ApprovalPolicyRepository.SaveChangesAsync</c> is always exactly one batch containing one
    /// <c>Modified</c> entity (the deactivated current version) and one <c>Added</c> entity (the new
    /// version) - never two <c>Modified</c> entities the way <c>BaselineRepository.TryActivateAsync</c>'s
    /// pre-fix single-batch shape did, which is what let EF's change-tracking order emit the activate
    /// UPDATE before the deactivate UPDATE roughly half the time. This test executes that exact
    /// production call shape against the real, current <see cref="ApprovalPolicyConfiguration"/> 30
    /// times to make the absence of ordering flakiness an executed fact, not an inference from reading
    /// the code once.</para>
    /// </summary>
    [Fact]
    public async Task Sequential_Update_Of_The_Tenant_Wide_Default_Policy_Never_Trips_The_New_Index_Across_30_Trials()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        for (var trial = 0; trial < 30; trial++)
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new FakeTenantProvider(tenantId);
            var now = DateTimeOffset.UtcNow;

            await using (var seedContext = CreateContext(connection, tenantProvider))
            {
                var v1 = ApprovalPolicy.CreateInitialVersion(
                    tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now.AddYears(-1), Rules);
                seedContext.ApprovalPolicies.Add(v1);
                await seedContext.SaveChangesAsync();
            }

            await using var context = CreateContext(connection, tenantProvider);
            var repository = new ApprovalPolicyRepository(context);

            // Mirrors UpdateApprovalPolicyCommandHandler.Handle verbatim.
            var current = await repository.FindActiveTenantDefaultAsync(ApprovalDocumentType.PaymentCertificate);
            Assert.NotNull(current);
            var nextVersion = current!.CreateNextVersion(now, Rules, allowSelfApproval: true, null, null);
            current.Deactivate(now);
            repository.AddVersion(nextVersion);

            var succeeded = await repository.SaveChangesAsync();
            Assert.True(succeeded);

            await using var verifyContext = CreateContext(connection, tenantProvider);
            var activeCount = await verifyContext.ApprovalPolicies
                .CountAsync(p => p.ProjectId == null && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive);
            Assert.Equal(1, activeCount);

            var activeId = await verifyContext.ApprovalPolicies
                .Where(p => p.ProjectId == null && p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive)
                .Select(p => p.Id)
                .SingleAsync();
            Assert.Equal(nextVersion.Id, activeId);
        }
    }

    /// <summary>
    /// This task's other explicit ask: confirm a legitimate project-scoped override still activates
    /// correctly under the second (`ProjectId IS NOT NULL`) index - a sequential, non-racing update,
    /// same handler shape as the tenant-default test above, but for a real, non-null <c>ProjectId</c>.
    /// Together with that test, this closes the ordering question for both of the split index's two
    /// groups, not just the one that happened to be broken before.
    /// </summary>
    [Fact]
    public async Task Sequential_Update_Of_A_Project_Scoped_Override_Policy_Activates_Correctly_Under_The_Second_Index()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        Guid v1Id;
        await using (var seedContext = CreateContext(connection, tenantProvider))
        {
            var v1 = ApprovalPolicy.CreateInitialVersion(
                tenantId, projectId, ApprovalDocumentType.VariationOrder, now.AddYears(-1), Rules);
            seedContext.ApprovalPolicies.Add(v1);
            await seedContext.SaveChangesAsync();
            v1Id = v1.Id;
        }

        await using (var context = CreateContext(connection, tenantProvider))
        {
            var repository = new ApprovalPolicyRepository(context);

            // FindActiveTenantDefaultAsync only ever looks at ProjectId == null, so it cannot find
            // this deliberately project-scoped row - loaded directly here purely to exercise the
            // same Deactivate + CreateNextVersion + AddVersion + SaveChangesAsync sequence the
            // handler uses, same as ApprovalPolicyActivationConcurrencySqliteTests' existing
            // project-scoped race test does for the identical reason.
            var current = await context.ApprovalPolicies.SingleAsync(p => p.Id == v1Id);
            var nextVersion = current.CreateNextVersion(now, Rules, allowSelfApproval: true, null, null);
            current.Deactivate(now);
            repository.AddVersion(nextVersion);

            var succeeded = await repository.SaveChangesAsync();
            Assert.True(succeeded);
        }

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.ApprovalPolicies
            .CountAsync(p => p.ProjectId == projectId && p.DocumentType == ApprovalDocumentType.VariationOrder && p.IsActive);
        Assert.Equal(1, activeCount);

        var activePolicy = await verifyContext.ApprovalPolicies
            .SingleAsync(p => p.ProjectId == projectId && p.DocumentType == ApprovalDocumentType.VariationOrder && p.IsActive);
        Assert.Equal(2, activePolicy.Version);

        var originalStillInactive = await verifyContext.ApprovalPolicies
            .Where(p => p.Id == v1Id)
            .Select(p => p.IsActive)
            .SingleAsync();
        Assert.False(originalStillInactive);
    }

    /// <summary>
    /// The whole point of splitting one index into two disjoint groups, proven directly: a tenant-wide
    /// default (<c>ProjectId = null</c>) and a project-scoped override (<c>ProjectId</c> = a real
    /// value) for the <em>same</em> <c>(TenantId, DocumentType)</c> must be able to coexist as
    /// simultaneously active, since they answer different routing questions
    /// (<c>ApprovalPolicyReader.GetCandidatePoliciesAsync</c> deliberately returns both as candidates -
    /// see <c>ApprovalPolicyConfiguration</c>'s own remarks on that query shape). Under a single,
    /// unsplit index keyed <c>(TenantId, ProjectId, DocumentType)</c> this would already have worked by
    /// accident (a null and a non-null <c>ProjectId</c> are never equal to each other either) - this
    /// test exists so a future, wrongly-merged index (e.g. someone "simplifying" back to one index
    /// without the <c>IS NULL</c>/<c>IS NOT NULL</c> filters) would still pass the two single-group
    /// tests above yet fail here, since a naive unfiltered merge could plausibly reintroduce a
    /// cross-group collision depending on how it was written.
    /// </summary>
    [Fact]
    public async Task TenantDefault_And_ProjectScoped_Override_For_The_Same_DocumentType_Coexist_As_Simultaneously_Active()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.UtcNow;

        await using var context = CreateContext(connection, tenantProvider);

        var tenantDefault = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, now, Rules);
        var projectOverride = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId, ApprovalDocumentType.PaymentCertificate, now, Rules);

        context.ApprovalPolicies.AddRange(tenantDefault, projectOverride);

        // Both in the SAME SaveChangesAsync batch - if the two indexes were not actually disjoint,
        // this single call would already fail.
        await context.SaveChangesAsync();

        await using var verifyContext = CreateContext(connection, tenantProvider);
        var activeCount = await verifyContext.ApprovalPolicies
            .CountAsync(p => p.DocumentType == ApprovalDocumentType.PaymentCertificate && p.IsActive);
        Assert.Equal(2, activeCount);
    }
}
