using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// Narrow, isolated discriminator for "is this <see cref="DbUpdateException"/> specifically a
/// unique-index/unique-constraint violation" - the one thing EF Core itself does not expose
/// provider-agnostically. EF only names one <see cref="DbUpdateException"/> subtype,
/// <see cref="DbUpdateConcurrencyException"/>, and that is reserved for optimistic-concurrency-token
/// mismatches (an expected-row-count check EF itself performs after a successful write) - a
/// completely different failure shape from a real DB engine rejecting a write outright. There is no
/// <c>DbUpdateUniqueConstraintException</c>; every provider reports a constraint violation as a bare
/// <see cref="DbUpdateException"/> wrapping a provider-specific <see cref="System.Data.Common.DbException"/>
/// whose shape only that provider defines.
///
/// <para><b>Why this exists instead of the caller widening its own catch to plain
/// <see cref="DbUpdateException"/>:</b> <c>ProjectRepository.TrySaveChangesAsync</c>'s own doc comment
/// already warns that catching <see cref="DbUpdateException"/> broadly risks mis-reporting an
/// unrelated failure (a bug, a different constraint, a transient connection fault) as "just a
/// concurrency conflict, retry" - silently hiding a real defect behind a misleading 409. This type
/// exists so a caller (<see cref="BaselineRepository.TryActivateAsync"/>) can react to exactly one
/// specific, well-understood failure shape and let everything else continue to propagate unhandled,
/// unchanged from before.</para>
///
/// <para><b>Why two provider branches, and why the second one is reflection-based rather than a
/// direct type reference:</b> production only ever talks to SQL Server -
/// <see cref="Microsoft.Data.SqlClient"/> is already a real, transitive, compile-time Infrastructure
/// dependency (via <c>Microsoft.EntityFrameworkCore.SqlServer</c>), so matching
/// <see cref="SqlException.Number"/> 2601/2627 costs nothing extra and needs no indirection. The only
/// executable proof available in this environment (no SQL Server reachable - Docker cannot start) is
/// the Integration test project's SQLite harness
/// (<c>Baseline/BaselineActivationOrderingSqliteTests.cs</c>), whose provider throws
/// <see cref="System.Data.Common.DbException"/> subtype <c>Microsoft.Data.Sqlite.SqliteException</c>
/// instead - but <c>Microsoft.Data.Sqlite</c> is deliberately referenced only by that test project
/// (<c>CMPlus.Integration.Tests.csproj</c>'s own comment: added narrowly for exactly this probe), never
/// by Infrastructure, so the shipped production assembly never carries an unrelated native SQLite
/// engine dependency it will never load. Matching that one test-only provider's error shape by exact
/// runtime type-name plus a single reflected property read keeps that dependency boundary intact
/// while still letting the SQLite harness exercise the real, shipped classifier end to end - not a
/// hand-rolled reimplementation of it.</para>
/// </summary>
internal static class UniqueIndexViolationClassifier
{
    // SQL Server: 2627 = "Violation of %ls constraint '%.*ls'... Cannot insert duplicate key in
    // object '%.*ls'" (a named PRIMARY KEY or UNIQUE constraint); 2601 = "Cannot insert duplicate key
    // row in object '%.*ls' with unique index '%.*ls'" (a unique INDEX specifically - this codebase's
    // actual shape everywhere: BaselineConfiguration/ApprovalPolicyConfiguration both declare
    // HasIndex(...).IsUnique(), never a named UNIQUE constraint). Both are checked, since which one a
    // given engine version/object type reports is not itself the thing being asserted here.
    private const int SqlServerDuplicateKeyUniqueIndex = 2601;
    private const int SqlServerDuplicateKeyConstraint = 2627;

    // SQLite: SqliteException.SqliteExtendedErrorCode 2067 == SQLITE_CONSTRAINT_UNIQUE
    // (SQLITE_CONSTRAINT(19) | (15 << 8)). Verified directly against the real
    // Microsoft.Data.Sqlite 10.0.10 package this solution pins (CMPlus.Integration.Tests.csproj) by
    // reproducing a real UNIQUE-index violation and inspecting the thrown exception's
    // SqliteExtendedErrorCode/SqliteErrorCode/Message - not taken from documentation alone. The plain
    // (non-extended) SqliteErrorCode is 19 (SQLITE_CONSTRAINT), which is not specific enough on its own
    // - it also covers NOT NULL/CHECK/FOREIGN KEY violations - so only the extended code is matched.
    private const int SqliteUniqueConstraintExtendedCode = 2067;

    private const string SqliteExceptionTypeFullName = "Microsoft.Data.Sqlite.SqliteException";

    /// <summary>
    /// <see langword="true"/> only for the two specific, well-understood "duplicate key against a
    /// unique index" shapes documented above. Anything else - including a null/absent inner exception,
    /// a different SQL Server error number, or an exception that merely has the right SQLite type name
    /// but an unexpected extended error code - returns <see langword="false"/>, so the caller lets it
    /// propagate as a genuine, unclassified <see cref="DbUpdateException"/> rather than ever silently
    /// reclassifying an unrelated failure as a conflict.
    /// </summary>
    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            SqlException sql => sql.Number is SqlServerDuplicateKeyUniqueIndex or SqlServerDuplicateKeyConstraint,
            { } inner => IsSqliteUniqueConstraintViolation(inner),
            null => false,
        };

    private static bool IsSqliteUniqueConstraintViolation(Exception inner)
    {
        if (inner.GetType().FullName != SqliteExceptionTypeFullName)
        {
            return false;
        }

        // Reflection, not a compile-time cast - see this type's remarks on why Infrastructure takes
        // no compile-time dependency on Microsoft.Data.Sqlite. Any shape mismatch (property
        // renamed/removed in a future Sqlite package version) fails safe to `false` - i.e. the
        // exception still propagates unhandled - rather than ever silently mis-classifying.
        var extendedCode = inner.GetType()
            .GetProperty("SqliteExtendedErrorCode", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(inner);

        return extendedCode is int code && code == SqliteUniqueConstraintExtendedCode;
    }
}
