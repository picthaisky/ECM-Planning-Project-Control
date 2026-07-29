using System.Runtime.CompilerServices;
using CMPlus.Infrastructure.Parsers.Excel;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// EPPlus 8 requires <see cref="OfficeOpenXml.ExcelPackage.License"/> configured once per process
/// before any workbook operation (see <see cref="ExcelPackageLicense"/> in Infrastructure). The
/// parser/hardening tests in this folder construct <see cref="OfficeOpenXml.ExcelPackage"/> directly
/// (no WebApi host / DI composition root in play, unlike <c>CustomWebApplicationFactory</c>-based
/// tests elsewhere in this same test process), so nothing else guarantees the license is configured
/// before they run. Routed through <see cref="ExcelPackageLicense.EnsureConfigured"/> - the same
/// process-wide, idempotency-guarded entry point <c>AddInfrastructure</c> uses - rather than calling
/// <c>ExcelPackage.License</c> directly here, so there is exactly one guard for "has this process
/// already set the license" no matter which code path (a test module initializer here, or a
/// <c>CustomWebApplicationFactory</c>-hosted test's DI composition) runs first.
/// </summary>
internal static class EpplusTestLicenseInitializer
{
    [ModuleInitializer]
    public static void Initialize() => ExcelPackageLicense.EnsureConfigured(new ExcelLicenseOptions());
}
