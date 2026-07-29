using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S2-BE-02: AuditLog is append-only and distinguishes "no actor" (system/seed writes)
/// from a real user.</summary>
public class AuditLogTests
{
    [Fact]
    public void Constructor_Assigns_All_Fields()
    {
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        var log = new AuditLog(tenantId, "Project", entityId, AuditAction.Updated, userId, "{\"a\":1}", "{\"a\":2}", timestamp);

        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal("Project", log.EntityName);
        Assert.Equal(entityId, log.EntityId);
        Assert.Equal(AuditAction.Updated, log.Action);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("{\"a\":1}", log.BeforeJson);
        Assert.Equal("{\"a\":2}", log.AfterJson);
        Assert.Equal(timestamp, log.Timestamp);
    }

    [Fact]
    public void UserId_Null_Means_No_Authenticated_Actor_Distinguishable_From_A_Real_User()
    {
        var log = new AuditLog(Guid.NewGuid(), "Tenant", Guid.NewGuid(), AuditAction.Created, null, null, "{}", DateTimeOffset.UtcNow);

        Assert.Null(log.UserId);
    }

    [Fact]
    public void Constructor_Rejects_Blank_EntityName()
    {
        Assert.Throws<DomainException>(() => new AuditLog(
            Guid.NewGuid(), "  ", Guid.NewGuid(), AuditAction.Created, null, null, "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters_Or_Mutating_Methods()
    {
        var propertiesWithPublicSetters = typeof(AuditLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .ToList();
        Assert.Empty(propertiesWithPublicSetters);

        var mutatingMethods = typeof(AuditLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AuditLog))
            .ToList();
        Assert.Empty(mutatingMethods);
    }
}
