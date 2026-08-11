using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Photos;

/// <summary>Shared hand-written fakes for the S12-BE-01 photo handler test suites - mirrors this
/// codebase's established shared-fakes-per-feature convention (see
/// <c>CMPlus.Application.Tests.Features.Issues.IssuesTestFakes</c>).</summary>
internal sealed class FakePhotoRepository : IPhotoRepository
{
    public bool ProjectExists { get; set; } = true;
    public HashSet<Guid> ExistingActivityIds { get; set; } = [];
    public List<Photo> Added { get; } = [];
    public int SaveChangesCallCount { get; private set; }

    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProjectExists);

    public Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExistingActivityIds.Contains(activityId));

    public void Add(Photo photo) => Added.Add(photo);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    public Task<Photo?> FindAsync(Guid photoId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Added.SingleOrDefault(p => p.Id == photoId));
}

/// <summary>In-memory <see cref="IFileStorage"/> double - records every <see cref="SaveAsync"/> call's
/// exact bytes (by reading the stream fully, exactly once, the same way a real disk/blob adapter
/// would) so a handler test can assert on what was ACTUALLY handed to storage, not merely that some
/// call happened.</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> SavedContent { get; } = [];
    public Dictionary<string, string> SavedContentTypes { get; } = [];

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        SavedContent[key] = buffer.ToArray();
        SavedContentTypes[key] = contentType;
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!SavedContent.TryGetValue(key, out var bytes))
        {
            throw new FileNotFoundException(key);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        SavedContent.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedContent.ContainsKey(key));
}

internal sealed class FakePhotoOptionsProvider(long maxFileSizeBytes = 10 * 1024 * 1024) : IPhotoOptionsProvider
{
    public long MaxFileSizeBytes { get; } = maxFileSizeBytes;
}

internal sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; } = tenantId;
}

internal sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role => UserRole.Site;
}

internal sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
