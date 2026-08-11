namespace CMPlus.Application.Abstractions;

/// <summary>
/// Exposes the configured idempotency-response replay cap to <c>IdempotencyMiddleware</c> (S13-BE-01),
/// same <c>IOptions&lt;T&gt;</c>-behind-a-narrow-interface pattern as <see cref="IPhotoOptionsProvider"/>/
/// <see cref="IImportOptionsProvider"/> - kept out of Application directly so Application never takes
/// a dependency on <c>Microsoft.Extensions.Options</c>/configuration wiring (ADR-0001), and kept out
/// of WebApi directly for the identical reason one layer further out: WebApi must not reach into
/// Infrastructure's concrete <c>IdempotencyOptions</c>/<c>IOptions&lt;T&gt;</c> binding either.
/// </summary>
public interface IIdempotencyOptionsProvider
{
    /// <summary>S13-BE-01 design note: "bounded - do not store megabyte photo responses". See
    /// <c>IdempotencyOptions</c>'s remarks for the default and why none of today's four wrapped
    /// endpoints can actually reach it.</summary>
    int MaxReplayableResponseBodyBytes { get; }
}
