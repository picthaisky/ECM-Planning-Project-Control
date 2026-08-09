using CMPlus.Application.Features.Gantt.Queries.GetGantt;
using FluentValidation.Results;

namespace CMPlus.Application.Tests.Features.Gantt;

public class GetGanttQueryValidatorTests
{
    private static ValidationResult Validate(GetGanttQuery query) =>
        new GetGanttQueryValidator().Validate(query);

    [Fact]
    public void A_Query_With_No_Date_Range_Has_No_Errors()
    {
        var result = Validate(new GetGanttQuery(Guid.NewGuid(), null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_Query_With_From_Before_To_Has_No_Errors()
    {
        var result = Validate(new GetGanttQuery(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-31T00:00:00Z")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void A_Query_With_Only_From_Or_Only_To_Has_No_Errors()
    {
        var onlyFrom = Validate(new GetGanttQuery(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        var onlyTo = Validate(new GetGanttQuery(Guid.NewGuid(), null, DateTimeOffset.UtcNow));

        Assert.True(onlyFrom.IsValid);
        Assert.True(onlyTo.IsValid);
    }

    [Fact]
    public void A_Query_With_From_After_To_Is_Rejected()
    {
        var result = Validate(new GetGanttQuery(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z")));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void An_Empty_ProjectId_Is_Rejected()
    {
        var result = Validate(new GetGanttQuery(Guid.Empty, null, null));

        Assert.False(result.IsValid);
    }
}
