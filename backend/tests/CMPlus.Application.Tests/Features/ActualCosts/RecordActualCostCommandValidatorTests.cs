using CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;
using CMPlus.Domain.Enums;
using FluentValidation.Results;

namespace CMPlus.Application.Tests.Features.ActualCosts;

public class RecordActualCostCommandValidatorTests
{
    private static ValidationResult Validate(RecordActualCostCommand command) =>
        new RecordActualCostCommandValidator().Validate(command);

    private static RecordActualCostCommand ValidCommand(
        decimal amount = 1_000_000.00m, DateTimeOffset? incurredDate = null, decimal? quantity = null) => new(
        Guid.NewGuid(),
        WbsNodeId: null,
        ActivityId: null,
        CostCategory.Subcontract,
        ActualCostEntryType.Actual,
        amount,
        incurredDate ?? DateTimeOffset.Parse("2026-01-31T00:00:00+07:00"),
        ReversesEntryId: null,
        DocumentReference: "INV-2026-0001",
        CostCode: "5100-100",
        VendorName: "ABC Subcontractor Co., Ltd.",
        Note: null,
        PaidDate: null,
        quantity,
        UnitOfMeasure: quantity is null ? null : "m3");

    [Fact]
    public void A_Valid_Entry_Has_No_Errors()
    {
        var result = Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_Empty_ProjectId_Is_Rejected()
    {
        var command = ValidCommand() with { ProjectId = Guid.Empty };

        Assert.False(Validate(command).IsValid);
    }

    [Fact]
    public void A_Zero_Amount_Is_Rejected()
    {
        // actual-cost.md §7.6: a zero cost entry, including a zero reversal, is noise.
        var command = ValidCommand(amount: 0m);

        Assert.False(Validate(command).IsValid);
    }

    [Fact]
    public void A_Negative_Amount_Is_Accepted_A_Reversal_Or_Credit_Note_Is_Legitimate()
    {
        var command = ValidCommand(amount: -600_000.00m);

        Assert.True(Validate(command).IsValid);
    }

    [Theory]
    [InlineData(100.005)]
    [InlineData(100.001)]
    public void An_Amount_With_More_Than_Two_Decimal_Places_Is_Rejected(double amount)
    {
        var command = ValidCommand(amount: (decimal)amount);

        Assert.False(Validate(command).IsValid);
    }

    [Fact]
    public void A_Default_IncurredDate_Is_Rejected()
    {
        // `with` (not the ValidCommand(incurredDate:) optional parameter) so the test genuinely
        // sends default(DateTimeOffset) through to the validator, rather than the helper's own
        // `??` fallback silently substituting a valid date for a null DateTimeOffset?.
        var command = ValidCommand() with { IncurredDate = default };

        Assert.False(Validate(command).IsValid);
    }

    [Fact]
    public void A_Negative_Quantity_Is_Rejected_When_Supplied()
    {
        var command = ValidCommand(quantity: -1m);

        Assert.False(Validate(command).IsValid);
    }

    [Fact]
    public void A_Null_Quantity_Is_Accepted()
    {
        var command = ValidCommand(quantity: null);

        Assert.True(Validate(command).IsValid);
    }

    [Theory]
    [InlineData((CostCategory)999)]
    public void An_Undefined_CostCategory_Is_Rejected(CostCategory invalid)
    {
        var command = ValidCommand() with { CostCategory = invalid };

        Assert.False(Validate(command).IsValid);
    }

    [Theory]
    [InlineData((ActualCostEntryType)999)]
    public void An_Undefined_EntryType_Is_Rejected(ActualCostEntryType invalid)
    {
        var command = ValidCommand() with { EntryType = invalid };

        Assert.False(Validate(command).IsValid);
    }
}
