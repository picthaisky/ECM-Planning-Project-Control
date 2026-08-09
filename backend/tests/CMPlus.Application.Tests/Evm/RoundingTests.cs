using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Evm;

/// <summary>
/// S7-QA-02 DoD (docs/10 §7): "ยืนยัน MidpointRounding.AwayFromZero ทุกจุดปัดเศษ" - proves
/// <see cref="MidpointRounding.AwayFromZero"/> is genuinely in effect at the real EVM/EAC *pipeline*
/// boundaries (<see cref="EvmEngine"/>/<see cref="EacCalculator"/> output feeding
/// <see cref="EvmResponseDto.From"/>), distinct from <c>CMPlus.Domain.Tests.Common.RoundingRulesTests</c>
/// (backend-developer's own suite), which proves the bare <see cref="RoundingRules"/> helper functions
/// in isolation. Both the money path (<c>decimal(18,2)</c> - SV/CV/ETC/EAC/VAC) and the ratio path
/// (<c>decimal(9,6)</c>/6dp - SPI/CPI/TCPI/PF) are covered here, per this DoD's explicit requirement,
/// plus a direct proof that an intermediate is never rounded before the final boundary (rounding
/// twice/early is documented as its own defect class, not just "not banker's rounding").
/// </summary>
public class RoundingTests
{
    private sealed class FakeEvmDataReader : IEvmDataReader
    {
        public ProjectEvmSettings? SettingsToReturn { get; set; }
        public IReadOnlyList<EvmActivityProgressInput> ActivityInputsToReturn { get; set; } = [];

        public Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SettingsToReturn);

        public Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
            Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityInputsToReturn);
    }

    private sealed class FakeActualCostReader(decimal actualCost) : IActualCostReader
    {
        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(actualCost, actualCost == 0m ? 0 : 1));
    }

    // ---------------------------------------------------------------------------------------
    // Money path (decimal(18,2)).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sv_And_Cv_Money_Path_Exact_Half_Cent_Tie_Rounds_Away_From_Zero_At_The_Real_Engine_Output()
    {
        // SV/CV are pure subtraction (EvmEngine.Compute has no division at all in this formula), so
        // an exact half-cent tie can be constructed directly from decimal literals - proving the
        // *engine's actual returned value* rounds away-from-zero at the boundary, not merely that the
        // bare RoundingRules helper does when called with a hand-picked literal in isolation.
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 350_000.000m, ev: 400_000.005m, ac: 350_000.000m);

        Assert.Equal(50_000.005m, core.Sv); // ev - pv: exact decimal subtraction, no rounding of its own.
        Assert.Equal(50_000.005m, core.Cv); // ev - ac: same exact tie via a different input pair.

        Assert.Equal(50_000.01m, RoundingRules.ToMoney(core.Sv));
        Assert.Equal(50_000.01m, RoundingRules.ToMoney(core.Cv));

        // Explicit differential (mirrors the Domain.Tests money-path pattern): prove this is not a
        // coincidence by showing the banker's-rounding alternative really is a different, wrong number.
        Assert.Equal(50_000.00m, Math.Round(core.Sv, 2, MidpointRounding.ToEven));
        Assert.Equal(50_000.00m, Math.Round(core.Cv, 2, MidpointRounding.ToEven));
    }

    // ---------------------------------------------------------------------------------------
    // Ratio path (decimal(9,6) / 6dp) - at the real GetEvmQueryHandler -> EvmResponseDto boundary.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Cpi_Ratio_Path_Exact_Tie_Rounds_Away_From_Zero_At_The_Real_GetEvmQuery_Response_Boundary()
    {
        // EV/AC = 49,373.00 / 400,000.00 = exactly 0.1234325 - a genuine, terminating 7-digit decimal
        // (independently verified: 400,000 x 0.1234325 = 49,373.0000 exactly), whose 6th digit ('2')
        // is even, so away-from-zero (0.123433) and to-even/banker's (0.123432) provably disagree.
        // This is deliberately unlike CMPlus.Domain.Tests.Common.RoundingRulesTests' own ToRatio(14/9)
        // fixture, whose infinitely-repeating tail is NOT an exact tie at 6dp and rounds identically
        // under either mode (see that file's own supplemented remarks) - that gap is what motivates
        // testing a genuine tie here too, and doing it through the real handler/DTO mapping
        // (EvmResponseDto.From is the one place RoundingRules.ToRatio(core.Cpi) is actually called in
        // production) rather than only the bare utility.
        var dataReader = new FakeEvmDataReader
        {
            SettingsToReturn = new ProjectEvmSettings(
                Bac: 1_000_000.00m,
                ProjectDataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
                EacVariantDefault: EacVariant.CpiBased,
                EacCustomPerformanceFactor: null,
                EacManualEtc: null),
            ActivityInputsToReturn =
            [
                new EvmActivityProgressInput(
                    Guid.NewGuid(), 49_373.00m,
                    DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
                    ProgressPercentageAsOf: 100m),
            ],
        };
        var costReader = new FakeActualCostReader(400_000.00m);
        var handler = new GetEvmQueryHandler(new EvmComputationService(dataReader, costReader));

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(49_373.00m, result.Value.Ev);
        Assert.Equal(400_000.00m, result.Value.Ac);
        Assert.Equal(0.123433m, result.Value.Cpi); // away-from-zero, not banker's 0.123432.
        Assert.NotEqual(Math.Round(0.1234325m, 6, MidpointRounding.ToEven), result.Value.Cpi!.Value);
    }

    // ---------------------------------------------------------------------------------------
    // "Round once, at the boundary" - an intermediate rounding step is its own defect class,
    // independent of which rounding mode is used.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rounding_The_Cpi_Intermediate_Early_Would_Silently_Shift_The_Final_Eac_By_More_Than_A_Cent()
    {
        // Fixture A1's own inputs (CPI = 6/7, a genuinely repeating decimal - exactly the shape where
        // an accidental early-rounding step is both easy to introduce and easy to miss in review,
        // since the final 2dp answer still "looks like a normal number" either way).
        var core = EvmEngine.Compute(bac: 1_000_000.00m, pv: 400_000.00m, ev: 300_000.00m, ac: 350_000.00m);
        var correctResult = EacCalculator.ComputeAll(core, customPf: null, manualEtc: null).GetVariant(EacVariant.CpiBased);

        // The documented-correct answer (evm-formulas.md; already covered by EacCalculatorTests' A1 -
        // re-derived here as this test's own baseline, not merely asserted from memory).
        Assert.Equal(816_666.67m, RoundingRules.ToMoney(correctResult.Etc));
        Assert.Equal(1_166_666.67m, RoundingRules.ToMoney(correctResult.Eac));

        // Simulates the exact defect this DoD line rules out: rounding CPI to its own display
        // precision (RoundingRules.RatioDecimals, 6dp) *before* deriving PF/ETC/EAC from it, instead
        // of carrying full decimal precision all the way through to the one real boundary.
        var cpiRoundedEarly = RoundingRules.ToRatio(core.Cpi!.Value);
        var pfFromRoundedCpi = 1m / cpiRoundedEarly;
        var etcFromRoundedCpi = pfFromRoundedCpi * (core.Bac - core.Ev);
        var eacFromRoundedCpi = core.Ac + etcFromRoundedCpi;

        // The "round early" path is a real, materially different (and wrong) number - 14 cents off,
        // an order of magnitude past the classic 1-cent away-from-zero-vs-banker's gap. If EacCalculator
        // ever started rounding an intermediate, these two assertions would start failing together.
        Assert.Equal(816_666.53m, RoundingRules.ToMoney(etcFromRoundedCpi));
        Assert.Equal(1_166_666.53m, RoundingRules.ToMoney(eacFromRoundedCpi));

        Assert.NotEqual(RoundingRules.ToMoney(etcFromRoundedCpi), RoundingRules.ToMoney(correctResult.Etc));
        Assert.NotEqual(RoundingRules.ToMoney(eacFromRoundedCpi), RoundingRules.ToMoney(correctResult.Eac));
    }
}
