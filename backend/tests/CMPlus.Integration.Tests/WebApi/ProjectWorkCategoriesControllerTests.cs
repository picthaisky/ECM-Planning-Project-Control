using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// End-to-end for <c>GET /api/v1/projects/{projectId}/work-categories</c> (the S12 catalogue gap
/// closure): the standard tenant-wide catalogue <see cref="WorkCategorySeeder"/> seeds for every
/// tenant is returned, ordered, over real HTTP + the real EF-backed reader.
/// </summary>
public class ProjectWorkCategoriesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private sealed record WorkCategoryResponse(Guid Id, string Code, string NameTh, string NameEn, int DisplayOrder);

    private readonly CustomWebApplicationFactory _factory;

    public ProjectWorkCategoriesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    private async Task<LoginResponse> LoginAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = DevDataSeeder.DevSeedPassword }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> GetSeededProjectIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CmPlusDbContext>();
        var project = await dbContext.Projects.IgnoreQueryFilters().FirstAsync(p => p.TenantId == tenantId);
        return project.Id;
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_With_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/work-categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Getting_The_Catalogue_Returns_The_Seeded_Standard_Set_Ordered_By_Display_Order()
    {
        using var client = _factory.CreateClient();
        var user = await LoginAsync(client, "site@siam-construction.dev");
        Authorize(client, user.AccessToken);
        var projectId = await GetSeededProjectIdAsync(user.TenantId);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/work-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<WorkCategoryResponse>>();
        Assert.NotNull(categories);
        Assert.Equal(7, categories!.Count); // the standard tenant-wide default set
        Assert.Equal("GEN", categories[0].Code); // DisplayOrder 1 first
        Assert.Equal(2, categories[1].DisplayOrder);
        var structural = Assert.Single(categories, c => c.Code == "STR");
        Assert.Equal("Structural", structural.NameEn);
        Assert.False(string.IsNullOrWhiteSpace(structural.NameTh)); // Thai name carried too
    }
}
