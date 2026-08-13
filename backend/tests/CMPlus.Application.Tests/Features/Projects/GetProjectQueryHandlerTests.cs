using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Projects.Queries.GetProject;
using CMPlus.Application.Features.Projects.Queries.GetProjects;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Projects;

public class GetProjectQueryHandlerTests
{
    private sealed class FakeReader(ProjectDetailDto? detail) : IProjectReader
    {
        public Task<IReadOnlyList<ProjectListItemDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProjectDetailDto?> GetDetailByIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(detail is not null && detail.Id == projectId ? detail : null);
    }

    private static ProjectDetailDto Detail(Guid id) => new(
        id, "โครงการทดสอบ", "P-001", "Owner",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), 100_000_000m, 100_000_000m,
        RetentionRate: 5m, AdvanceRate: 10m, RetentionCapPercentage: null, RetentionRelease1Percentage: 50m,
        DefectsLiabilityMonths: 12, AdvanceAmountPaid: null, AdvanceRecoveryMethod.ProRata,
        AdvanceRecoveryStartPct: null, AdvanceRecoveryRatePct: null, AdvanceRecoveryEndPct: null,
        EacVariant.CpiBased, EacManualEtc: null, EacCustomPerformanceFactor: null, EacManualEtcStaleSince: null);

    [Fact]
    public async Task Handle_Returns_The_Projects_Detail_When_It_Exists_In_The_Tenant()
    {
        var id = Guid.NewGuid();
        var handler = new GetProjectQueryHandler(new FakeReader(Detail(id)));

        var result = await handler.Handle(new GetProjectQuery(id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal(EacVariant.CpiBased, result.Value.EacVariantDefault); // EAC config is included
    }

    [Fact]
    public async Task Handle_Returns_NotFound_For_An_Unknown_Or_Cross_Tenant_Id()
    {
        // Reader yields null (the global query filter hides other tenants) -> a bare 404, never an
        // empty success that would confirm the id exists somewhere.
        var handler = new GetProjectQueryHandler(new FakeReader(null));

        var result = await handler.Handle(new GetProjectQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.NotFound, result.Error);
    }
}
