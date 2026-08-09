using CMPlus.Domain.Common;

namespace CMPlus.Domain.Tests.Common;

/// <summary>
/// S7-BE-01/02 critical-correctness requirement: every rounding point in the EVM/EAC pipeline must
/// use <see cref="MidpointRounding.AwayFromZero"/>, never the unqualified
/// <see cref="Math.Round(decimal, int)"/> default (<see cref="MidpointRounding.ToEven"/> - "banker's
/// rounding"). domain-decisions.md §2.1's worked example is the canonical proof this distinction is
/// real money, not academic: 1,000,000.10 at a 5% rate away-from-zero rounds to 50,000.01; banker's
/// rounds the same input to 50,000.00 - a genuine 0.01 discrepancy against SQL Server's `ROUND()` and
/// a printed certificate.
/// </summary>
public class RoundingRulesTests
{
    [Theory]
    [InlineData(50_000.005, 50_000.01)] // the canonical domain-decisions.md §2.1 trap.
    [InlineData(50_000.015, 50_000.02)]
    [InlineData(-50_000.005, -50_000.01)] // away-from-zero, not merely "round up".
    [InlineData(1_166_666.665, 1_166_666.67)]
    public void ToMoney_Rounds_Exact_Half_Cents_Away_From_Zero_Not_To_Even(decimal input, decimal expected)
    {
        Assert.Equal(expected, RoundingRules.ToMoney(input));
    }

    [Fact]
    public void ToMoney_Would_Differ_From_Bankers_Rounding_On_The_Canonical_Trap()
    {
        // Demonstrates the actual defect this rule exists to prevent: the unqualified Math.Round
        // default (ToEven) rounds 50,000.005 down to 50,000.00 (the digit before is already even),
        // which is the wrong, non-compliant answer for this codebase.
        var bankersRounded = Math.Round(50_000.005m, 2, MidpointRounding.ToEven);
        var awayFromZeroRounded = RoundingRules.ToMoney(50_000.005m);

        Assert.Equal(50_000.00m, bankersRounded);
        Assert.Equal(50_000.01m, awayFromZeroRounded);
        Assert.NotEqual(bankersRounded, awayFromZeroRounded);
    }

    [Fact]
    public void ToMoney_Of_Null_Is_Null_Not_Zero()
    {
        Assert.Null(RoundingRules.ToMoney((decimal?)null));
    }

    [Fact]
    public void ToRatio_Rounds_To_Six_Decimal_Places_Away_From_Zero()
    {
        // 14/9 = 1.5555555555555555555555555556 (28-digit decimal division result) -> 1.555556 at
        // 6dp away-from-zero (the 7th digit is 5, rounds the 6th digit up).
        var fourteenNinths = 14m / 9m;

        Assert.Equal(1.555556m, RoundingRules.ToRatio(fourteenNinths));
    }

    /// <summary>
    /// S7-QA-02 gap found on independent review: <see cref="ToRatio_Rounds_To_Six_Decimal_Places_Away_From_Zero"/>
    /// asserts a correct value, but 14/9's infinitely-repeating-5 tail beyond the 6th decimal place
    /// (...5555555...) is strictly greater than an exact half at that position, so *both*
    /// <see cref="MidpointRounding.AwayFromZero"/> and <see cref="MidpointRounding.ToEven"/> round it
    /// to the identical 1.555556 (independently verified). That existing test would keep passing even
    /// if <see cref="RoundingRules.ToRatio(decimal)"/> regressed to the unqualified (banker's)
    /// <see cref="Math.Round(decimal, int)"/> overload, so it does not actually prove which rounding
    /// mode is in effect. This test supplies a genuine exact tie at the 6th decimal place instead -
    /// 0.1234325 is an exact, terminating decimal *literal* (not a division result with a
    /// non-terminating tail), and its 6th digit ('2') is even, so away-from-zero (0.123433) and
    /// to-even (0.123432) provably disagree. Money's own canonical trap (50,000.005, above) already
    /// had this property; the ratio path did not until now.
    /// </summary>
    [Fact]
    public void ToRatio_Rounds_A_Genuine_Six_Decimal_Tie_Away_From_Zero_Not_To_Even()
    {
        const decimal exactTie = 0.1234325m;

        var awayFromZero = RoundingRules.ToRatio(exactTie);
        var bankersRounded = Math.Round(exactTie, 6, MidpointRounding.ToEven);

        Assert.Equal(0.123433m, awayFromZero);
        Assert.Equal(0.123432m, bankersRounded);
        Assert.NotEqual(bankersRounded, awayFromZero);
    }

    [Fact]
    public void ToRatio_Of_Null_Is_Null_Not_Zero()
    {
        Assert.Null(RoundingRules.ToRatio((decimal?)null));
    }

    [Fact]
    public void ToPercentage_Rounds_A_Genuine_Two_Decimal_Tie_Away_From_Zero_Not_To_Even()
    {
        // S8-BE-02: same genuine-exact-tie discipline as ToRatio's own test above - 60.005's 2nd
        // decimal digit ('0') is even, so away-from-zero (60.01) and to-even (60.00) provably
        // disagree; a division-result tail (e.g. a repeating decimal) would not prove which mode is
        // actually in effect.
        const decimal exactTie = 60.005m;

        var awayFromZero = RoundingRules.ToPercentage(exactTie);
        var bankersRounded = Math.Round(exactTie, 2, MidpointRounding.ToEven);

        Assert.Equal(60.01m, awayFromZero);
        Assert.Equal(60.00m, bankersRounded);
        Assert.NotEqual(bankersRounded, awayFromZero);
    }

    [Fact]
    public void ToPercentage_Of_Null_Is_Null_Not_Zero()
    {
        Assert.Null(RoundingRules.ToPercentage((decimal?)null));
    }
}
