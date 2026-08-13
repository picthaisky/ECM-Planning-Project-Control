using CMPlus.Application.Features.Manpower.Queries.ListWorkCategories;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Tests.Features.Manpower;

public class ListWorkCategoriesQueryHandlerTests
{
    [Fact]
    public async Task Handle_Maps_The_Repositorys_Categories_To_Dtos_Preserving_Order()
    {
        var repository = new FakeManpowerEquipmentLogRepository
        {
            // The repository is responsible for ordering (DisplayOrder); the handler preserves it.
            WorkCategoriesToReturn =
            [
                new WorkCategory(Guid.NewGuid(), projectId: null, "GEN", "งานทั่วไป", "General", 1),
                new WorkCategory(Guid.NewGuid(), projectId: null, "STR", "งานโครงสร้าง", "Structural", 2),
            ],
        };
        var handler = new ListWorkCategoriesQueryHandler(repository);

        var result = await handler.Handle(new ListWorkCategoriesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            first => Assert.Equal("GEN", first.Code),
            second =>
            {
                Assert.Equal("STR", second.Code);
                Assert.Equal("งานโครงสร้าง", second.NameTh);
                Assert.Equal("Structural", second.NameEn);
                Assert.Equal(2, second.DisplayOrder);
            });
    }

    [Fact]
    public async Task Handle_Returns_An_Empty_List_Never_Fails_When_There_Are_No_Categories()
    {
        var handler = new ListWorkCategoriesQueryHandler(new FakeManpowerEquipmentLogRepository());

        var result = await handler.Handle(new ListWorkCategoriesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
