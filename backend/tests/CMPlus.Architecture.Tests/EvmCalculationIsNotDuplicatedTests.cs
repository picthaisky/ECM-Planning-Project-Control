using System.Reflection;
using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Evm;
using NetArchTest.Rules;

namespace CMPlus.Architecture.Tests;

/// <summary>
/// S8-BE-01's DoD ("ไม่มีการคำนวณซ้ำนอก EVM engine" - no calculation duplicated outside the EVM engine)
/// pinned as a permanent fitness function, not just a code-review rule and the handlers' own doc-
/// comment promise (see <see cref="GetCashFlowQueryHandler"/>'s remarks). <see cref="GetCashFlowQueryHandler"/>
/// and <see cref="GetDashboardQueryHandler"/> must route every PV/EV/AC-shaped figure through
/// <see cref="EvmComputationService"/> - the one legitimate holder of
/// <c>IEvmDataReader</c>/<c>IActualCostReader</c>/<c>EvmEngine</c>/<c>EacCalculator</c> - and must
/// never acquire a second, independent route to the same raw inputs or the same arithmetic.
///
/// <para><b>Why a structural check, not just a numeric one:</b> a numeric cross-screen comparison
/// (see <c>CMPlus.Integration.Tests.Dashboard.CrossScreenNumericConsistencyTests</c>) only catches a
/// duplicated-calculation regression when the duplicate implementation happens to disagree with the
/// real engine on that specific fixture - two independently-written calculations can coincidentally
/// agree, especially on round numbers, and would keep agreeing right up until a fixture happened to
/// expose an edge case. A dependency-shape assertion instead catches the entire regression class the
/// moment a duplicate dependency is introduced - a build failure at PR time, not a latent bug waiting
/// for the wrong fixture. The two instruments are complementary, not substitutes for each other: this
/// file proves the *shape* is right; the numeric suite proves the *values* are right given that shape.</para>
/// </summary>
public class EvmCalculationIsNotDuplicatedTests
{
    private static readonly Assembly ApplicationAssembly = typeof(EvmComputationService).Assembly;

    public static IEnumerable<object[]> CashFlowAndDashboardHandlers =>
    [
        [nameof(GetCashFlowQueryHandler), typeof(GetCashFlowQueryHandler).FullName!],
        [nameof(GetDashboardQueryHandler), typeof(GetDashboardQueryHandler).FullName!],
    ];

    /// <summary>
    /// The only legitimate caller of the raw EVM primitives/engine is <see cref="EvmComputationService"/>
    /// itself (S7-BE-03's own "reader(s) -&gt; EvmEngine -&gt; EacCalculator" pipeline) - anything else
    /// acquiring them directly has built a second, independently-drifting calculation path.
    /// Deliberately listed as exact full type names, not namespace prefixes, so this does not also
    /// flag the *result* record types both handlers legitimately consume (e.g.
    /// <c>GetDashboardQueryHandler</c> reading an already-computed <c>EacVariantResult</c> off
    /// <c>EacCalculationResult</c>, both in the <c>CMPlus.Application.Services.Evm</c> namespace, is
    /// fine - calling <c>EacCalculator</c>/<c>EvmEngine</c> themselves is not).
    /// </summary>
    private static readonly string[] RawEvmPrimitivesAndEngine =
    [
        "CMPlus.Application.Abstractions.IActualCostReader",
        "CMPlus.Application.Abstractions.IEvmDataReader",
        "CMPlus.Application.Services.Evm.EvmEngine",
        "CMPlus.Application.Services.Evm.EacCalculator",
        // Belt-and-braces: today this is *also* guaranteed at the project-reference level (Application
        // carries no EF Core PackageReference at all, so a direct DbContext dependency here could not
        // even compile) - kept explicit anyway because it is exactly backend-developer's own claim
        // ("issues no query against a DbContext of its own"), and an assertion that fails with a
        // readable message naming the offending type is a strictly stronger regression fence than a
        // guarantee that only holds "as long as nobody ever changes the project file".
        "Microsoft.EntityFrameworkCore",
    ];

    [Theory]
    [MemberData(nameof(CashFlowAndDashboardHandlers))]
    public void Handler_Type_Is_Found_Exactly_Once_Sanity_Check_For_The_Fitness_Functions_Below(string shortName, string fullName)
    {
        // If a rename ever broke this filter, the two rules below would otherwise pass *vacuously*
        // (NetArchTest's Should()/ShouldNot() are trivially satisfied over an empty type set) and
        // silently stop testing anything at all - this guards against exactly that.
        var matched = Types.InAssembly(ApplicationAssembly).That().HaveName([shortName]).GetTypes().ToList();

        var found = Assert.Single(matched);
        Assert.Equal(fullName, found.FullName);
    }

    [Theory]
    [MemberData(nameof(CashFlowAndDashboardHandlers))]
    public void Handler_Depends_On_EvmComputationService_Sanity_Check_The_Rule_Below_Is_Not_Vacuous(string shortName, string fullName)
    {
        _ = fullName; // only the short name is needed for the NetArchTest filter itself.
        var result = Types.InAssembly(ApplicationAssembly)
            .That().HaveName([shortName])
            .Should().HaveDependencyOn("CMPlus.Application.Features.Evm.EvmComputationService")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(CashFlowAndDashboardHandlers))]
    public void Handler_Never_Depends_On_The_Raw_Evm_Primitives_Or_Engine_Directly(string shortName, string fullName)
    {
        _ = fullName;
        var result = Types.InAssembly(ApplicationAssembly)
            .That().HaveName([shortName])
            .ShouldNot().HaveDependencyOnAny(RawEvmPrimitivesAndEngine)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null ? "unknown failure" : string.Join(", ", result.FailingTypeNames);
}
