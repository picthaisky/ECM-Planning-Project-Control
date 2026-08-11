using System.Text.RegularExpressions;

namespace CMPlus.Architecture.Tests;

/// <summary>
/// S15-SEC-01 finding L-01, pinned as a permanent fitness function.
///
/// <para>The entire cross-tenant guarantee (ADR-0002) rests on the ambient EF Core global query
/// filter applied to every <c>ITenantOwned</c> type. The only two constructs that can disable it are
/// <c>IgnoreQueryFilters()</c> and raw SQL (<c>FromSql*</c>/<c>ExecuteSql*</c>/<c>ExecuteUpdate*</c>/
/// <c>ExecuteDelete*</c>, which bypass the LINQ filter entirely). Every sprint's tenant-isolation
/// review had, until now, re-established by hand that these appear only where they are deliberately
/// and safely used — a manual grep that no test enforced, so a future reader/handler that reached for
/// one without re-asserting the tenant boundary would silently leak and nothing would fail.</para>
///
/// <para>This test converts that manual check into a build-time guard, exactly as the reflection-
/// driven <c>TenantIsolationTests</c> converts "did anyone add an <c>ITenantOwned</c> type without
/// coverage" into a loud failure. Each sanctioned site is enumerated with its reason; adding a new
/// bypass forces a deliberate edit here, which is the review gate. The scan strips <c>//</c> comment
/// tails first, so the many doc-comment *mentions* of these constructs (which explain why a given
/// class does or does not use them) are correctly ignored — only real call sites count.</para>
/// </summary>
public class TenantIsolationBypassGuardTests
{
    // Sanctioned IgnoreQueryFilters() call sites (S15-SEC-01 §1.1), by file name:
    //  - UserReader.cs        : login happens before any tenant is known; returns a projection only.
    //  - EfIdempotencyStore.cs: the retention sweep is a background job with no ambient per-request
    //                           tenant, and reclaims expired dedup rows across every tenant by design.
    private static readonly HashSet<string> SanctionedIgnoreQueryFiltersFiles =
        new(StringComparer.OrdinalIgnoreCase) { "UserReader.cs", "EfIdempotencyStore.cs" };

    // Sanctioned raw-SQL call sites (S15-SEC-01 §1.1), by file name:
    //  - CpmScheduleRepository.cs: the one bulk write; parameterized ExecuteSqlInterpolatedAsync that
    //                              re-asserts WHERE a.TenantId = {tenant} because raw SQL bypasses the
    //                              LINQ filter. This is the required re-assertion, not a leak.
    private static readonly HashSet<string> SanctionedRawSqlFiles =
        new(StringComparer.OrdinalIgnoreCase) { "CpmScheduleRepository.cs" };

    private static readonly Regex IgnoreQueryFiltersCall =
        new(@"\.IgnoreQueryFilters\s*\(", RegexOptions.Compiled);

    private static readonly Regex RawSqlCall =
        new(@"\.(FromSql|ExecuteSql|ExecuteUpdate|ExecuteDelete)\w*\s*\(", RegexOptions.Compiled);

    [Fact]
    public void IgnoreQueryFilters_Appears_Only_In_Sanctioned_Sites()
    {
        var offenders = InfrastructureSourceFiles()
            .Where(f => IgnoreQueryFiltersCall.IsMatch(StripLineComments(File.ReadAllText(f))))
            .Select(Path.GetFileName)
            .Where(name => !SanctionedIgnoreQueryFiltersFiles.Contains(name!))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "IgnoreQueryFilters() is a tenant-filter bypass and may appear only in " +
            $"[{string.Join(", ", SanctionedIgnoreQueryFiltersFiles)}] (ADR-0002 / S15-SEC-01 §1.1). " +
            $"New call site(s) found in: {string.Join(", ", offenders)}. If deliberate and safe, add " +
            "the file to SanctionedIgnoreQueryFiltersFiles with its justification and record it in the " +
            "next tenant-isolation review.");
    }

    [Fact]
    public void Raw_Sql_Appears_Only_In_Sanctioned_Sites()
    {
        var offenders = InfrastructureSourceFiles()
            .Where(f => RawSqlCall.IsMatch(StripLineComments(File.ReadAllText(f))))
            .Select(Path.GetFileName)
            .Where(name => !SanctionedRawSqlFiles.Contains(name!))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Raw SQL (FromSql*/ExecuteSql*/ExecuteUpdate*/ExecuteDelete*) bypasses the LINQ tenant " +
            $"filter and may appear only in [{string.Join(", ", SanctionedRawSqlFiles)}] " +
            "(ADR-0002 / S15-SEC-01 §1.1), where it re-asserts the tenant boundary explicitly. New " +
            $"call site(s) found in: {string.Join(", ", offenders)}. If deliberate, it MUST re-assert " +
            "WHERE TenantId = {tenant}; then add the file to SanctionedRawSqlFiles and record it.");
    }

    /// <summary>Sanity: the guard is not vacuous — the sanctioned files genuinely contain the calls,
    /// so a scan that silently found nothing (wrong path, comment-stripping too aggressive) fails.</summary>
    [Fact]
    public void The_Guard_Is_Not_Vacuous_The_Sanctioned_Sites_Really_Contain_The_Constructs()
    {
        var files = InfrastructureSourceFiles().ToArray();
        Assert.NotEmpty(files);

        Assert.Contains(files, f =>
            Path.GetFileName(f)!.Equals("EfIdempotencyStore.cs", StringComparison.OrdinalIgnoreCase)
            && IgnoreQueryFiltersCall.IsMatch(StripLineComments(File.ReadAllText(f))));

        Assert.Contains(files, f =>
            Path.GetFileName(f)!.Equals("CpmScheduleRepository.cs", StringComparison.OrdinalIgnoreCase)
            && RawSqlCall.IsMatch(StripLineComments(File.ReadAllText(f))));
    }

    private static IEnumerable<string> InfrastructureSourceFiles()
    {
        var root = SolutionRelativePath(Path.Combine("src", "CMPlus.Infrastructure"));
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Removes the tail of every single-line (<c>//</c>) comment, including XML-doc (<c>///</c>)
    /// lines, so a construct named in prose is not mistaken for a call. Block comments are not used
    /// around these constructs anywhere in this codebase; kept deliberately simple.</summary>
    private static string StripLineComments(string source)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0)
            {
                lines[i] = lines[i][..idx];
            }
        }

        return string.Join('\n', lines);
    }

    private static string SolutionRelativePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate CMPlus.sln from the test output directory.");
        }

        return Path.Combine(dir.FullName, relative);
    }
}
