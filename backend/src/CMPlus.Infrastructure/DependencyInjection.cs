using CMPlus.Application.Abstractions;
using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Import;
using CMPlus.Infrastructure.Parsers.Excel;
using CMPlus.Infrastructure.Parsers.Mspdi;
using CMPlus.Infrastructure.Parsers.Xer;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Infrastructure.Photos;
using CMPlus.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Infrastructure;

/// <summary>
/// Composition root helper for Infrastructure registrations. Sprint 1 delivered the DbContext and
/// the progress reader; Sprint 2 (S2-BE-01/02/04-07) adds JWT issuance, password hashing, the
/// audit interceptor, and the approval-policy/user readers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();

        // S9-BE-01: PaymentCertificate.RowVersion concurrency-token generation - see the
        // interceptor's own remarks for why this is needed at all (InMemory only) and why it is
        // harmless against real SQL Server (which always wins with its own server-generated value).
        services.AddScoped<RowVersionSaveChangesInterceptor>();

        // Security review sprint-09.md M-01: structurally enforces IAppendOnly (ApprovalAction,
        // AuditLog, ProjectFinanceLedger, ActualCostEntry, EvmPeriodSnapshot) at SavingChanges -
        // append-only was previously a C#-API convention only, with no persistence-layer control.
        services.AddScoped<AppendOnlyGuardInterceptor>();

        services.AddDbContext<CmPlusDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("CmPlusDatabase"))
                   .AddInterceptors(
                       provider.GetRequiredService<AuditSaveChangesInterceptor>(),
                       provider.GetRequiredService<RowVersionSaveChangesInterceptor>(),
                       provider.GetRequiredService<AppendOnlyGuardInterceptor>()));

        services.AddScoped<IActivityProgressReader, ActivityProgressReader>();
        services.AddScoped<IUserReader, UserReader>();
        services.AddScoped<IApprovalPolicyReader, ApprovalPolicyReader>();

        // S4-BE-01..03: WBS tree read, Project master-data edit, batch progress write.
        services.AddScoped<IWbsTreeReader, WbsTreeReader>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IBatchProgressRepository, BatchProgressRepository>();

        // S4-BE-04/05 (gap closure): tenant project list, activities-under-a-node read.
        services.AddScoped<IProjectReader, ProjectReader>();
        services.AddScoped<IWbsNodeActivitiesReader, WbsNodeActivitiesReader>();

        // S5-BE-04: CPM schedule graph load + bulk result write-back.
        services.AddScoped<ICpmScheduleRepository, CpmScheduleRepository>();

        // ADR-0019: read side of the append-only CpmRun history - the governing-run lookup
        // S11-BE-02's EotEvaluator will consume.
        services.AddScoped<ICpmRunHistoryReader, CpmRunHistoryReader>();

        // S6-BE-01: Gantt activity-bar read (the WBS hierarchy read it also needs is IWbsTreeReader
        // above, reused rather than duplicated).
        services.AddScoped<IGanttActivityReader, GanttActivityReader>();

        // S7-BE-01/03/05: EVM/EAC engine data sources.
        services.AddScoped<IEvmDataReader, EvmDataReader>();
        services.AddScoped<IEvmSnapshotRepository, EvmSnapshotRepository>();

        // S8 (actual-cost.md, ADR-0013): ActualCostEntry ledger read/write. IActualCostReader now
        // has a real, indexed implementation - the Sprint 7 literal-0 placeholder is gone.
        services.AddScoped<IActualCostReader, ActualCostReader>();
        services.AddScoped<IActualCostRepository, ActualCostRepository>();

        // S8-BE-02: Executive Dashboard's WBS weight-based progress rollup (evm-formulas.md's
        // "Progress rollup (WBS tree)" rule) - the one extra read WbsProgressRollupCalculator needs
        // beyond IWbsTreeReader, which it reuses rather than duplicates.
        services.AddScoped<IWbsProgressReader, WbsProgressReader>();

        // S9-BE-04 (payment-retention.md §4): retention/advance ledger SUM() reader - the "one
        // source of truth" R^cum/D^cum the CertificateCalculator/AdvanceRecoveryCalculator inputs
        // are resolved from.
        services.AddScoped<IProjectFinanceLedgerReader, ProjectFinanceLedgerReader>();

        // S9-BE-05/06: PaymentCertificate approval-chain command handlers + the Tenant Admin
        // policy-write API - the first real consumers of Sprint 2's ApprovalPolicy/ApprovalAction
        // model and RowVersion concurrency token.
        services.AddScoped<IPaymentCertificateRepository, PaymentCertificateRepository>();
        services.AddScoped<IApprovalActionRepository, ApprovalActionRepository>();
        services.AddScoped<IApprovalPolicyRepository, ApprovalPolicyRepository>();

        // S10-BE-01/02/03: VariationOrder approval-chain command handlers - reuses the Sprint 2/9
        // ApprovalPolicy/ApprovalAction/RowVersion machinery unchanged.
        services.AddScoped<IVariationOrderRepository, VariationOrderRepository>();

        // S11-BE-01/03: immutable DailyWeatherLog (IAppendOnly, guarded by AppendOnlyGuardInterceptor
        // registered above) and the IssueLog Open->Doing->Closed state machine.
        services.AddScoped<IDailyWeatherLogRepository, DailyWeatherLogRepository>();
        services.AddScoped<IIssueLogRepository, IssueLogRepository>();

        // S11-BE-02: EotEvaluator's persistence boundary - project calendar/policy/activity context
        // reads plus the append-only EotEvaluation write (IAppendOnly, guarded by
        // AppendOnlyGuardInterceptor registered above).
        services.AddScoped<IEotEvaluationRepository, EotEvaluationRepository>();

        // S12-BE-01 (US-12.1): photo upload/serve. IFileStorage is the ADR-0010 seam - this is the
        // local-disk dev/staging adapter; no cloud-provider SDK type appears anywhere on this path
        // (Architecture.Tests enforces this structurally).
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        services.AddOptions<PhotoOptions>().Bind(configuration.GetSection(PhotoOptions.SectionName));
        services.AddSingleton<IPhotoOptionsProvider, PhotoOptionsProvider>();
        services.AddScoped<IPhotoRepository, PhotoRepository>();

        // S12-BE-02 (US-12.2): append-only ManpowerEquipmentLog (IAppendOnly, guarded by
        // AppendOnlyGuardInterceptor registered above) and the Productivity Index read side.
        services.AddScoped<IManpowerEquipmentLogRepository, ManpowerEquipmentLogRepository>();
        services.AddScoped<IProductivityIndexReader, ProductivityIndexReader>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // S3-BE-01..04: file import (XER/MSPDI/Excel) + FileImportJob history.
        services.AddOptions<ImportOptions>().Bind(configuration.GetSection(ImportOptions.SectionName));
        services.AddSingleton<IImportOptionsProvider, ImportOptionsProvider>();
        services.AddScoped<IImportRepository, ImportRepository>();
        services.AddSingleton<IXerScheduleParser, XerScheduleParser>();
        services.AddSingleton<IMspdiScheduleParser, MspdiScheduleParser>();
        services.AddSingleton<IExcelProgressImporter, ExcelProgressImporter>();
        services.AddSingleton<IExcelProgressTemplateWriter, ExcelProgressTemplateWriter>();

        // EPPlus 8 requires its license configured once per process before any workbook operation
        // (see ExcelPackageLicense's doc comment for the production licensing gap this flags).
        ExcelPackageLicense.EnsureConfigured(
            configuration.GetSection(ExcelLicenseOptions.SectionName).Get<ExcelLicenseOptions>() ?? new ExcelLicenseOptions());

        // S2-SEC-01 finding M-02: plain Configure<JwtOptions> binds but never validates - a missing
        // or short SigningKey previously surfaced only as an obscure failure on the first
        // authenticated request, not at boot. ValidateOnStart() runs this at host startup instead
        // (via a registered IHostedService), failing fast the same way a genuinely-missing
        // required setting should. 32 chars (256 bits) is the minimum HS256 key size to resist a
        // brute-force key search, not an arbitrary round number.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey must be set.")
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters (256 bits) for HS256.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be set.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be set.")
            .Validate(o => o.ExpiryMinutes > 0, "Jwt:ExpiryMinutes must be positive.")
            .ValidateOnStart();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
