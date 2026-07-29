using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class UserTests
{
    [Fact]
    public void Constructor_Rejects_Invalid_Email()
    {
        Assert.Throws<DomainException>(() =>
            new User(Guid.NewGuid(), "not-an-email", UserRole.PM, "hash"));
    }

    [Fact]
    public void Constructor_Normalises_Email_To_Lowercase()
    {
        var user = new User(Guid.NewGuid(), "Someone@Example.com", UserRole.PM, "hash");

        Assert.Equal("someone@example.com", user.Email);
    }

    [Fact]
    public void ChangeRole_Supports_ProjectDirector()
    {
        var user = new User(Guid.NewGuid(), "pd@example.com", UserRole.PM, "hash");

        user.ChangeRole(UserRole.ProjectDirector);

        Assert.Equal(UserRole.ProjectDirector, user.Role);
    }

    [Fact]
    public void Constructor_Rejects_Blank_PasswordHash()
    {
        Assert.Throws<DomainException>(() => new User(Guid.NewGuid(), "a@b.com", UserRole.PM, ""));
    }
}
