using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;
using CMPlus.Application.Wbs;
using CMPlus.Domain.Common;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;

namespace CMPlus.Integration.Tests.Dashboard;

/// <summary>
/// S8-QA-01: constructs the real MediatR handlers for the EVM / Cash Flow / Dashboard / WBS-node-
/// activities screens, wired to the real Infrastructure reader/repository implementations (never a
/// fake or mock), each against its own brand-new <see cref="CmPlusDbContext"/> opened fresh from the
/// supplied <see cref="TestDbContextFactory"/> for that one call only.
///
/// <para>Every cross-screen test in this folder calls through here rather than newing up a handler
/// once and reusing it (or reusing one <c>DbContext</c>/<c>EvmComputationService</c> instance across
/// "independent" reads). That matters for what these tests are trying to prove: two response DTOs
/// that merely happen to share a literal pre-computed value (e.g. one <c>EvmComputation</c> object
/// fed into two DTO factories in the test itself) would agree trivially and prove nothing about
/// wiring. Routing every read through a fresh <see cref="CmPlusDbContext"/> and a fresh handler
/// instance means each call is a genuinely independent round trip through that screen's own real
/// public entry point (<c>IRequestHandler&lt;,&gt;.Handle</c>) - the same shape three separate HTTP
/// requests for three separate screens would have in production, and EF Core's first-level identity
/// map cannot mask a real bug by silently reusing a tracked instance across them.</para>
/// </summary>
internal static class RealHandlerFactory
{
    public static async Task<Result<EvmResponseDto>> GetEvmAsync(TestDbContextFactory factory, GetEvmQuery query)
    {
        using var dbContext = factory.CreateContext();
        var handler = new GetEvmQueryHandler(CreateEvmComputationService(dbContext));
        return await handler.Handle(query, CancellationToken.None);
    }

    public static async Task<Result<CashFlowResponseDto>> GetCashFlowAsync(TestDbContextFactory factory, GetCashFlowQuery query)
    {
        using var dbContext = factory.CreateContext();
        var handler = new GetCashFlowQueryHandler(CreateEvmComputationService(dbContext), new EvmSnapshotRepository(dbContext));
        return await handler.Handle(query, CancellationToken.None);
    }

    public static async Task<Result<DashboardResponseDto>> GetDashboardAsync(TestDbContextFactory factory, GetDashboardQuery query)
    {
        using var dbContext = factory.CreateContext();
        var handler = new GetDashboardQueryHandler(
            CreateEvmComputationService(dbContext), new WbsTreeReader(dbContext), new WbsProgressReader(dbContext));
        return await handler.Handle(query, CancellationToken.None);
    }

    public static async Task<Result<IReadOnlyList<ActivityForProgressDto>>> GetNodeActivitiesAsync(
        TestDbContextFactory factory, GetNodeActivitiesQuery query)
    {
        using var dbContext = factory.CreateContext();
        var handler = new GetNodeActivitiesQueryHandler(new WbsNodeActivitiesReader(dbContext));
        return await handler.Handle(query, CancellationToken.None);
    }

    private static EvmComputationService CreateEvmComputationService(CmPlusDbContext dbContext) =>
        new(new EvmDataReader(dbContext), new ActualCostReader(dbContext));
}
