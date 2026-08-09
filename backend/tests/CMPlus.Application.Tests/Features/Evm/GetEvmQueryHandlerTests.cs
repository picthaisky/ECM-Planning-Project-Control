using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Evm;

public class GetEvmQueryHandlerTests
{
    private sealed class FakeEvmDataReader : IEvmDataReader
    {
        public ProjectEvmSettings? SettingsToReturn { get; set; }
        public IReadOnlyList<EvmActivityProgressInput> ActivityInputsToReturn { get; set; } = [];
        public DateTimeOffset? LastAsOfRequested { get; private set; }

        public Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SettingsToReturn);

        public Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
            Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastAsOfRequested = asOf;
            return Task.FromResult(ActivityInputsToReturn);
        }
    }

    private sealed class FakeActualCostReader : IActualCostReader
    {
        public decimal ActualCostToReturn { get; set; }
        public int EntryCountToReturn { get; set; } = 1;
        public DateTimeOffset? LastAsOfRequested { get; private set; }

        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastAsOfRequested = asOf;
            return Task.FromResult(new ActualCostResult(ActualCostToReturn, EntryCountToReturn));
        }
    }

    private static ProjectEvmSettings DefaultSettings(
        EacVariant defaultVariant = EacVariant.CpiBased, DateTimeOffset? projectDataDate = null) => new(
        Bac: 1_000_000.00m,
        ProjectDataDate: projectDataDate ?? DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
        EacVariantDefault: defaultVariant,
        EacCustomPerformanceFactor: null,
        EacManualEtc: null);

    private static (GetEvmQueryHandler Handler, FakeEvmDataReader DataReader, FakeActualCostReader CostReader) CreateHandler(
        ProjectEvmSettings? settings, decimal actualCost = 350_000.00m)
    {
        var dataReader = new FakeEvmDataReader { SettingsToReturn = settings };
        var costReader = new FakeActualCostReader { ActualCostToReturn = actualCost };
        var computationService = new EvmComputationService(dataReader, costReader);
        return (new GetEvmQueryHandler(computationService), dataReader, costReader);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, _, _) = CreateHandler(settings: null);

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EvmErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_An_Unrecognised_EacVariant_With_A_400_Never_A_Silent_Fallback()
    {
        var (handler, _, _) = CreateHandler(DefaultSettings());

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, "NotARealVariant"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(EvmErrorCodes.InvalidEacVariantPrefix, result.Error);
        Assert.Contains("NotARealVariant", result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_A_Numeric_EacVariant_String_Even_Though_It_Is_A_Defined_Enum_Value()
    {
        // EacVariantParser is deliberately stricter than a plain Enum.TryParse - "3" must not
        // silently resolve to EacVariant.CpiSpiBased (see EacVariantParser's remarks).
        var (handler, _, _) = CreateHandler(DefaultSettings());

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, "3"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(EvmErrorCodes.InvalidEacVariantPrefix, result.Error);
    }

    [Theory]
    [InlineData("Atypical")]
    [InlineData("atypical")]
    [InlineData("ATYPICAL")]
    public async Task Handle_Accepts_The_EacVariant_Override_Case_Insensitively(string rawVariant)
    {
        var (handler, _, _) = CreateHandler(DefaultSettings(defaultVariant: EacVariant.CpiBased));

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, rawVariant), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EacVariant.Atypical, result.Value.SelectedVariant);
    }

    [Fact]
    public async Task Handle_Defaults_SelectedVariant_To_The_Projects_Own_Default_When_No_Override_Is_Given()
    {
        var (handler, _, _) = CreateHandler(DefaultSettings(defaultVariant: EacVariant.CpiSpiBased));

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EacVariant.CpiSpiBased, result.Value.SelectedVariant);
    }

    [Fact]
    public async Task Handle_Always_Returns_Every_Variant_Regardless_Of_Which_One_Is_Selected()
    {
        var (handler, _, _) = CreateHandler(DefaultSettings());

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), null, "Atypical"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Enum.GetValues<EacVariant>().Length, result.Value.Variants.Count);
        Assert.All(Enum.GetValues<EacVariant>(), v => Assert.Contains(result.Value.Variants, r => r.Variant == v));
    }

    [Fact]
    public async Task Handle_Defaults_The_Data_Date_To_The_Projects_Own_DataDate_When_Omitted()
    {
        var expectedDataDate = DateTimeOffset.Parse("2026-05-31T00:00:00+07:00");
        var (handler, dataReader, costReader) = CreateHandler(DefaultSettings(projectDataDate: expectedDataDate));

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), DataDate: null, EacVariant: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedDataDate, result.Value.DataDate);
        Assert.Equal(expectedDataDate, dataReader.LastAsOfRequested);
        Assert.Equal(expectedDataDate, costReader.LastAsOfRequested);
    }

    [Fact]
    public async Task Handle_Forwards_An_Explicit_Data_Date_To_Both_Readers()
    {
        var explicitDataDate = DateTimeOffset.Parse("2026-03-15T00:00:00+07:00");
        var (handler, dataReader, costReader) = CreateHandler(DefaultSettings());

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), explicitDataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(explicitDataDate, result.Value.DataDate);
        Assert.Equal(explicitDataDate, dataReader.LastAsOfRequested);
        Assert.Equal(explicitDataDate, costReader.LastAsOfRequested);
    }

    [Fact]
    public async Task Handle_Computes_Ev_And_Pv_From_The_Activity_Inputs_Not_Zero()
    {
        var dataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");
        var dataReader = new FakeEvmDataReader
        {
            SettingsToReturn = DefaultSettings(projectDataDate: dataDate),
            ActivityInputsToReturn =
            [
                new EvmActivityProgressInput(
                    Guid.NewGuid(), 1_000_000.00m,
                    DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-12-31T00:00:00+07:00"),
                    30m),
            ],
        };
        var costReader = new FakeActualCostReader { ActualCostToReturn = 350_000.00m };
        var handler = new GetEvmQueryHandler(new EvmComputationService(dataReader, costReader));

        var result = await handler.Handle(new GetEvmQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(300_000.00m, result.Value.Ev); // 1,000,000 * 30%.
        Assert.Equal(350_000.00m, result.Value.Ac);
    }
}
