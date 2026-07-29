using CMPlus.Application.Abstractions;
using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Import;
using CMPlus.Infrastructure.Parsers.Excel;
using CMPlus.Infrastructure.Parsers.Mspdi;
using CMPlus.Infrastructure.Parsers.Xer;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
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

        services.AddDbContext<CmPlusDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("CmPlusDatabase"))
                   .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));

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
