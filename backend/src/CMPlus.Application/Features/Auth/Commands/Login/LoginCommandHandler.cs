using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Auth.Commands.Login;

/// <summary>
/// S2-BE-01. Returns the same generic <c>InvalidCredentials</c> failure whether the email does
/// not exist or the password is wrong - never reveals which, so login cannot be used to enumerate
/// registered emails.
/// </summary>
public sealed class LoginCommandHandler(
    IUserReader userReader,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<LoginCommand, Result<LoginResult>>
{
    private const string InvalidCredentials = "InvalidCredentials";

    public async Task<Result<LoginResult>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await userReader.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return Result<LoginResult>.Failure(InvalidCredentials);
        }

        var token = jwtTokenService.GenerateToken(user.TenantId, user.Id, user.Role);

        return Result<LoginResult>.Success(new LoginResult(
            token.AccessToken, token.ExpiresAt, user.Id, user.TenantId, user.Role));
    }
}
