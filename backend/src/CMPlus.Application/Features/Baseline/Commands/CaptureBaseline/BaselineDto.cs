namespace CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;

public sealed record BaselineDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    bool IsActive,
    DateTimeOffset CapturedAt,
    Guid CapturedByUserId,
    decimal Bac,
    int ActivityCount)
{
    public static BaselineDto From(CMPlus.Domain.Entities.Baseline baseline) => new(
        baseline.Id, baseline.ProjectId, baseline.Name, baseline.IsActive, baseline.CapturedAt,
        baseline.CapturedByUserId, baseline.Bac, baseline.ActivityCount);
}
