namespace CMPlus.Application.Abstractions;

/// <summary>
/// Resolves the current request's tenant. Implemented in Infrastructure/WebApi by reading the
/// authenticated user's JWT claim - never from client-supplied request input (ADR-0002). The EF
/// Core global query filter and every command handler that stamps <c>TenantId</c> on a new row
/// depend on this; a handler that reads TenantId from the request body/route instead is a
/// release-blocking bug.
/// </summary>
public interface ITenantProvider
{
    Guid TenantId { get; }
}
