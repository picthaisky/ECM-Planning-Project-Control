using CMPlus.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Storage;

/// <summary>
/// Local-disk <see cref="IFileStorage"/> adapter (ADR-0010's dev/local implementation - the
/// S3-API-compatible staging/production adapter is written only after the Sprint 16 provider gate).
/// No cloud-provider SDK type anywhere in this file or its signature - it is plain
/// <c>System.IO</c>, matching <see cref="IFileStorage"/>'s own BCL-only contract.
///
/// <para><b>"Outside the webroot" / "never a static-file passthrough" (S12-BE-01 DoD):</b> this
/// solution has no <c>wwwroot</c> folder and no <c>UseStaticFiles()</c> call anywhere
/// (<c>CMPlus.WebApi/Program.cs</c>), so nothing under <see cref="FileStorageOptions.LocalRootPath"/>
/// is reachable by a bare URL regardless of where that root points - the only way to read a photo's
/// bytes is <see cref="OpenReadAsync"/>, called exclusively from
/// <c>GetPhotoContentQueryHandler</c> after its own tenant/project ownership check.</para>
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.LocalRootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No stored content for key '{key}'.", path);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolvePath(key)));

    /// <summary>Defense-in-depth path-traversal guard. Every current caller passes a key derived
    /// entirely server-side (<c>Photo.StorageKey</c> - see that type's remarks: it is built from
    /// <c>TenantId</c>/<c>ProjectId</c>/<c>Id</c>/<c>Format</c> only, never client input), so this
    /// should never actually trigger - it exists so a future caller cannot accidentally reintroduce
    /// a traversal by passing a less-trusted key without anyone noticing.</summary>
    private string ResolvePath(string key)
    {
        var combined = Path.GetFullPath(Path.Combine(_rootPath, key));
        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage key '{key}' resolves outside the configured storage root.");
        }

        return combined;
    }
}
