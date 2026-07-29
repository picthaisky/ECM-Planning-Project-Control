namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// Resolves golden-file fixtures under <c>backend/tests/fixtures/goldenfiles/</c> (docs/10 §6
/// S3-QA-01 artifact path) by walking up from the test output directory to the solution root - the
/// same technique <c>CMPlus.Architecture.Tests.LayeringTests</c> uses to find <c>CMPlus.sln</c>, so
/// fixtures are read straight from source rather than needing an MSBuild copy-to-output item per file.
///
/// <para><b>Provenance (read before trusting these as production sign-off evidence):</b> every file
/// under <c>goldenfiles/</c> is a hand-built, format-spec-compliant synthetic fixture authored for
/// this sprint - not sourced from a real Primavera P6 or MS Project export. No licensed P6/MSP
/// installation was available in this environment to produce a genuine reference export. Expected
/// values are hand-computed against the XER/MSPDI field semantics documented in this sprint's
/// parser code, not verified against real software output. This is flagged as a real validation gap
/// (same pattern as <c>.claude/knowledge/domain/evm-formulas.md</c> §1.8's EAC-reconciliation
/// caveat) that must be closed - re-run this golden-file suite against an actual P6/MSP-exported
/// file - before production golden-file sign-off.</para>
/// </summary>
internal static class FixtureFiles
{
    public static string Read(string relativePathUnderGoldenFiles) =>
        File.ReadAllText(ResolvePath(relativePathUnderGoldenFiles));

    public static Stream OpenRead(string relativePathUnderGoldenFiles) =>
        File.OpenRead(ResolvePath(relativePathUnderGoldenFiles));

    private static string ResolvePath(string relativePathUnderGoldenFiles)
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

        return Path.Combine(dir.FullName, "tests", "fixtures", "goldenfiles", relativePathUnderGoldenFiles);
    }
}
