namespace CMPlus.Application.Abstractions;

/// <summary>
/// Blob/photo storage abstraction (ADR-0010). The AWS-vs-Azure decision is deferred to a Sprint
/// 16 gate, so no cloud-provider-specific type may appear anywhere in this signature - only
/// BCL types. Infrastructure provides a local-disk adapter for dev and an S3-API-compatible
/// adapter for staging/production; the concrete provider adapter is written only after the gate.
/// </summary>
public interface IFileStorage
{
    /// <summary>Persists <paramref name="content"/> under <paramref name="key"/>, overwriting any existing content at that key. Returns the stored path/key.</summary>
    Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens a readable stream for the content stored at <paramref name="key"/>.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
