using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Auth.Commands.Login;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Auth;

/// <summary>S2-BE-01: login succeeds only for a matching email+password, and never reveals
/// whether the failure was "no such email" versus "wrong password" (same generic error either
/// way, so login cannot be used to enumerate registered emails).</summary>
public class LoginCommandHandlerTests
{
    private sealed class FakeUserReader(UserAuthRecord? record) : IUserReader
    {
        public Task<UserAuthRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(record is not null && record.Email == email ? record : null);
    }

    /// <summary>Trivial reversible "hash" - real hashing is Pbkdf2PasswordHasherTests' concern
    /// (Infrastructure); this handler test only needs IPasswordHasher's contract honoured.</summary>
    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash::{password}";

        public bool Verify(string hash, string password) => hash == $"hash::{password}";
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public JwtToken GenerateToken(Guid tenantId, Guid userId, UserRole role) =>
            new($"token-for-{userId}", DateTimeOffset.UtcNow.AddHours(1));
    }

    private static LoginCommandHandler CreateHandler(UserAuthRecord? seededUser) =>
        new(new FakeUserReader(seededUser), new FakePasswordHasher(), new FakeJwtTokenService());

    [Fact]
    public async Task Handle_Returns_A_Token_With_All_Three_Claims_Worth_Of_Data_For_Correct_Credentials()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var user = new UserAuthRecord(userId, tenantId, "pm@tenant.dev", UserRole.PM, "hash::Secret123!");
        var handler = CreateHandler(user);

        var result = await handler.Handle(new LoginCommand("pm@tenant.dev", "Secret123!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(UserRole.PM, result.Value.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
    }

    [Fact]
    public async Task Handle_Fails_With_A_Generic_Error_When_The_Email_Does_Not_Exist()
    {
        var handler = CreateHandler(seededUser: null);

        var result = await handler.Handle(new LoginCommand("nobody@tenant.dev", "whatever"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InvalidCredentials", result.Error);
    }

    [Fact]
    public async Task Handle_Fails_With_The_Same_Generic_Error_When_The_Password_Is_Wrong()
    {
        var user = new UserAuthRecord(Guid.NewGuid(), Guid.NewGuid(), "pm@tenant.dev", UserRole.PM, "hash::Secret123!");
        var handler = CreateHandler(user);

        var result = await handler.Handle(new LoginCommand("pm@tenant.dev", "WrongPassword"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InvalidCredentials", result.Error);
    }
}
