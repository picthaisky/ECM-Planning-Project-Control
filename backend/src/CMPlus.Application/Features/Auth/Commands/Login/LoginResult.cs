using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Auth.Commands.Login;

public sealed record LoginResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    Guid TenantId,
    UserRole Role);
