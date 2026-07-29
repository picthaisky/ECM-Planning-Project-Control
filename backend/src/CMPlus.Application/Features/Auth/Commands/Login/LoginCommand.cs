using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Auth.Commands.Login;

/// <summary>
/// Email/password login (S2-BE-01). Deliberately carries no <c>tenantId</c> field at all - the
/// tenant is derived from the resolved user, never accepted as client input (ADR-0002 applies to
/// every mutating/authenticating operation, not only to already-authenticated requests).
/// </summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<Result<LoginResult>>;
