using OfficeOpenXml;

namespace CMPlus.Infrastructure.Parsers.Excel;

/// <summary>Configuration for EPPlus 8's mandatory runtime license declaration - read from
/// <c>Excel</c> configuration section, never hardcoded.</summary>
public sealed class ExcelLicenseOptions
{
    public const string SectionName = "Excel";

    /// <summary>Set only once CM+ has a real commercial EPPlus license key (CM+ is a commercial
    /// SaaS product - this is the production-correct value). Takes priority over the noncommercial
    /// fields below when present.</summary>
    public string? CommercialLicenseKey { get; set; }

    /// <summary>Polyform Noncommercial 1.0.0 default for dev/CI/evaluation only - see the
    /// backend-developer Sprint 3 report for why this must not ship to production unreplaced.
    /// EPPlus validates this as a plain name (letters/digits/spaces only - no <c>+</c>/punctuation),
    /// hence the plain-English spelling here rather than the product's usual "CM+" branding.</summary>
    public string NonCommercialOrganizationName { get; set; } = "CM Plus Project Control Dev Eval";
}

/// <summary>
/// EPPlus 8 refuses any workbook operation until <see cref="ExcelPackage.License"/> has been
/// configured for the process (dual Polyform Noncommercial / commercial license model). Set exactly
/// once per process from <c>DependencyInjection.AddInfrastructure</c> - guarded with our own flag
/// (not relying on whatever EPPlus itself does on a repeat call) because
/// <c>WebApplicationFactory</c>-based integration tests construct the host, and therefore call
/// <c>AddInfrastructure</c>, more than once within the same test process.
///
/// <para><b>Production gap, flagged deliberately (same pattern as the golden-file provenance
/// caveat):</b> CM+ is a commercial product; the <see cref="ExcelLicenseOptions.NonCommercialOrganizationName"/>
/// default below is legally valid only for non-commercial/evaluation use under Polyform
/// Noncommercial 1.0.0. A real <see cref="ExcelLicenseOptions.CommercialLicenseKey"/> must be
/// configured before this path ships to production - this is a licensing decision for a human
/// (legal/procurement), not something this agent can resolve.</para>
/// </summary>
public static class ExcelPackageLicense
{
    private static readonly Lock Gate = new();
    private static bool _configured;

    public static void EnsureConfigured(ExcelLicenseOptions options)
    {
        if (_configured)
        {
            return;
        }

        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.CommercialLicenseKey))
            {
                ExcelPackage.License.SetCommercial(options.CommercialLicenseKey);
            }
            else
            {
                ExcelPackage.License.SetNonCommercialOrganization(options.NonCommercialOrganizationName);
            }

            _configured = true;
        }
    }
}
