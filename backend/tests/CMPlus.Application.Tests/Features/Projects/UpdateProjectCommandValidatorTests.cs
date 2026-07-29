using CMPlus.Application.Features.Projects.Commands.UpdateProject;
using CMPlus.Domain.Enums;
using FluentValidation.Results;

namespace CMPlus.Application.Tests.Features.Projects;

public class UpdateProjectCommandValidatorTests
{
    private static UpdateProjectCommand ValidCommand() => new(
        ProjectId: Guid.NewGuid(),
        Name: "Name",
        Code: "P-001",
        Owner: "Owner Co.",
        ContractStart: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        ContractFinish: DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
        Bac: 1_000_000m,
        ContractValue: 1_000_000m,
        RetentionRate: 5.00m,
        AdvanceRate: 10.00m,
        RetentionCapPercentage: 10.00m,
        RetentionRelease1Percentage: 50.00m,
        DefectsLiabilityMonths: 12,
        AdvanceAmountPaid: null,
        AdvanceRecoveryMethod: AdvanceRecoveryMethod.ProRata,
        AdvanceRecoveryStartPct: null,
        AdvanceRecoveryRatePct: null,
        AdvanceRecoveryEndPct: null);

    private static ValidationResult Validate(UpdateProjectCommand command) =>
        new UpdateProjectCommandValidator().Validate(command);

    [Fact]
    public void A_Fully_Valid_Command_Has_No_Errors()
    {
        var result = Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ContractFinish_Before_ContractStart_Is_Rejected()
    {
        var command = ValidCommand() with
        {
            ContractStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            ContractFinish = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        };

        var result = Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.ContractFinish));
    }

    [Fact]
    public void ContractFinish_Equal_To_ContractStart_Is_Accepted()
    {
        var same = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var command = ValidCommand() with { ContractStart = same, ContractFinish = same };

        var result = Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public void RetentionRate_Outside_0_100_Is_Rejected(decimal rate)
    {
        var command = ValidCommand() with { RetentionRate = rate };

        var result = Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.RetentionRate));
    }

    [Fact]
    public void RetentionRate_Null_Is_Accepted_Not_Rejected()
    {
        var command = ValidCommand() with { RetentionRate = null };

        var result = Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public void AdvanceRate_Outside_0_100_Is_Rejected(decimal rate)
    {
        var command = ValidCommand() with { AdvanceRate = rate };

        var result = Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.AdvanceRate));
    }

    [Fact]
    public void AdvanceRate_Null_Is_Accepted_Not_Rejected()
    {
        // S4-QA-02 gap: only RetentionRate's null-acceptance was covered - AdvanceRate is nullable
        // for the identical "unset != 0" reason (Project.cs remarks) and must behave the same way
        // at this layer, not merely by assumption because RetentionRate does.
        var command = ValidCommand() with { AdvanceRate = null };

        var result = Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Negative_Bac_Is_Rejected()
    {
        var command = ValidCommand() with { Bac = -1m };

        var result = Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.Bac));
    }

    [Fact]
    public void Blank_Name_Code_Or_Owner_Is_Rejected()
    {
        var result = Validate(ValidCommand() with { Name = "", Code = "", Owner = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.Name));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.Code));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProjectCommand.Owner));
    }
}
