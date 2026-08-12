using CMPlus.Infrastructure.Storage;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// sprint-12/15 security review L-03: the <see cref="LocalDiskFileStorage"/> traversal guard compared
/// the resolved path against the root with <c>OrdinalIgnoreCase</c>, which is correct on Windows/NTFS
/// (case-insensitive) but wrong on the case-sensitive Linux filesystem ADR-0010 targets — there
/// <c>.../data</c> and <c>.../Data</c> are different directories, so a key resolving to <c>.../data/x</c>
/// under a <c>.../Data</c> root is a genuine escape that <c>OrdinalIgnoreCase</c> silently allowed.
/// <see cref="LocalDiskFileStorage.IsPathWithinRoot"/> now takes the filesystem case-sensitivity
/// explicitly, so both modes are proven here regardless of the host OS running the test.
/// </summary>
public class LocalDiskFileStoragePathGuardTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cmplus-guard-Data"));

    private static string Under(params string[] segments) =>
        Path.GetFullPath(Path.Combine(new[] { Path.GetTempPath() }.Concat(segments).ToArray()));

    [Fact]
    public void A_Legitimate_Subpath_Is_Within_The_Root_In_Both_Case_Modes()
    {
        var candidate = Path.GetFullPath(Path.Combine(Root, "tenant", "project", "photo.jpg"));

        Assert.True(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: true));
        Assert.True(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: false));
    }

    [Fact]
    public void A_Genuine_Escape_To_A_Different_Directory_Is_Blocked_In_Both_Case_Modes()
    {
        var candidate = Under("cmplus-guard-Elsewhere", "escaped.txt");

        Assert.False(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: true));
        Assert.False(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: false));
    }

    [Fact]
    public void A_Sibling_Directory_Sharing_The_Root_As_A_Name_Prefix_Is_Blocked_In_Both_Modes()
    {
        // Classic ".../store" vs ".../store-evil" bypass: the separator-boundary check must reject it.
        var candidate = Under("cmplus-guard-Data-evil", "x.txt");

        Assert.False(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: true));
        Assert.False(LocalDiskFileStorage.IsPathWithinRoot(candidate, Root, filesystemIsCaseSensitive: false));
    }

    [Fact]
    public void A_Case_Differing_Sibling_Is_Blocked_On_A_Case_Sensitive_Filesystem_But_Allowed_On_A_Case_Insensitive_One()
    {
        // The exact L-03 finding: root ".../Data", key resolving to ".../data/escaped.txt". On Linux
        // (case-sensitive) this is a different directory → must be blocked; on Windows (case-insensitive)
        // it is the same directory → correctly allowed. The old unconditional OrdinalIgnoreCase allowed
        // it everywhere, which was the defect.
        var caseDifferingCandidate = Under("cmplus-guard-data", "escaped.txt"); // 'data', root is 'Data'

        Assert.False(
            LocalDiskFileStorage.IsPathWithinRoot(caseDifferingCandidate, Root, filesystemIsCaseSensitive: true),
            "On a case-sensitive filesystem, .../data must NOT be treated as within .../Data (the L-03 fix).");
        Assert.True(
            LocalDiskFileStorage.IsPathWithinRoot(caseDifferingCandidate, Root, filesystemIsCaseSensitive: false),
            "On a case-insensitive filesystem, .../data and .../Data are the same directory, so it is within.");
    }
}
