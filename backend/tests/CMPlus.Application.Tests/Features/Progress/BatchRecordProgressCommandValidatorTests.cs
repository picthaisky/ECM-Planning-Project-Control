using CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;
using FluentValidation.Results;

namespace CMPlus.Application.Tests.Features.Progress;

public class BatchRecordProgressCommandValidatorTests
{
    private static ValidationResult Validate(BatchRecordProgressCommand command) =>
        new BatchRecordProgressCommandValidator().Validate(command);

    [Fact]
    public void A_Valid_Batch_Has_No_Errors()
    {
        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
            [new BatchProgressEntry(Guid.NewGuid(), 50m, 12.5m), new BatchProgressEntry(Guid.NewGuid(), 100m, null)]);

        var result = Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_Empty_Batch_Is_Rejected()
    {
        var command = new BatchRecordProgressCommand(Guid.NewGuid(), DateTimeOffset.UtcNow, []);

        var result = Validate(command);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public void A_ProgressPercentage_Outside_0_100_Is_Rejected(decimal pct)
    {
        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(), DateTimeOffset.UtcNow, [new BatchProgressEntry(Guid.NewGuid(), pct, null)]);

        var result = Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_Negative_ActualQuantity_Is_Rejected()
    {
        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(), DateTimeOffset.UtcNow, [new BatchProgressEntry(Guid.NewGuid(), 50m, -1m)]);

        var result = Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void An_Empty_ActivityId_Is_Rejected()
    {
        var command = new BatchRecordProgressCommand(
            Guid.NewGuid(), DateTimeOffset.UtcNow, [new BatchProgressEntry(Guid.Empty, 50m, null)]);

        var result = Validate(command);

        Assert.False(result.IsValid);
    }
}
