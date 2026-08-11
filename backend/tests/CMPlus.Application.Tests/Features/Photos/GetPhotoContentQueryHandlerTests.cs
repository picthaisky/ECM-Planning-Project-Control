using CMPlus.Application.Features.Photos;
using CMPlus.Application.Features.Photos.Queries.GetPhotoContent;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMPlus.Application.Tests.Features.Photos;

public class GetPhotoContentQueryHandlerTests
{
    private static Photo NewPhoto(Guid tenantId, Guid projectId) =>
        new(tenantId, projectId, null, null, PhotoImageFormat.Jpeg, 3, Guid.NewGuid(), DateTimeOffset.UtcNow, null);

    private static GetPhotoContentQueryHandler CreateHandler(FakePhotoRepository repository, FakeFileStorage storage) =>
        new(repository, storage, NullLogger<GetPhotoContentQueryHandler>.Instance);

    [Fact]
    public async Task Handle_Returns_NotFound_For_An_Unknown_PhotoId()
    {
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage();
        var handler = CreateHandler(repository, storage);

        var result = await handler.Handle(new GetPhotoContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.NotFound, result.Error);
    }

    // ------------------------------------------------------------------------------------
    // S12-SEC-01: "photo URLs must not be guessable in a way that permits cross-tenant access" -
    // FakePhotoRepository.FindAsync deliberately does NOT itself filter by tenant (the real
    // PhotoRepository relies on CmPlusDbContext's ambient global query filter for that, proven
    // separately at the integration level against a real DbContext) - so this handler-level test
    // instead proves the SECOND, independent check this handler itself performs: a real photo id
    // from a DIFFERENT PROJECT than the one in the route is treated identically to an unknown id,
    // never served.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_The_Identical_NotFound_When_The_Photo_Belongs_To_A_Different_Project()
    {
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage();
        var tenantId = Guid.NewGuid();
        var actualProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var photo = NewPhoto(tenantId, actualProjectId);
        repository.Add(photo);
        storage.SavedContent[photo.StorageKey] = [1, 2, 3];

        var handler = CreateHandler(repository, storage);
        var result = await handler.Handle(new GetPhotoContentQuery(otherProjectId, photo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.NotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_The_Stored_Bytes_And_The_Servers_Own_Computed_ContentType()
    {
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage();
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var photo = NewPhoto(tenantId, projectId);
        repository.Add(photo);
        byte[] scrubbedBytes = [10, 20, 30, 40];
        storage.SavedContent[photo.StorageKey] = scrubbedBytes;

        var handler = CreateHandler(repository, storage);
        var result = await handler.Handle(new GetPhotoContentQuery(projectId, photo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(scrubbedBytes, result.Value.Content);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.EndsWith(".jpg", result.Value.FileName, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------
    // L-04 (security review sprint-12.md): a Photo row that exists but whose blob is missing from
    // storage must degrade to the same NotFound Result the handler already returns for an unknown
    // id - never let FakeFileStorage's FileNotFoundException (the same exception type the real
    // LocalDiskFileStorage throws) escape the handler as an unhandled exception.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_NotFound_Rather_Than_Throwing_When_The_Row_Exists_But_Its_Blob_Is_Missing()
    {
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage(); // deliberately never populated for this photo's StorageKey.
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var photo = NewPhoto(tenantId, projectId);
        repository.Add(photo);

        var handler = CreateHandler(repository, storage);
        var result = await handler.Handle(new GetPhotoContentQuery(projectId, photo.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.NotFound, result.Error);
    }
}
