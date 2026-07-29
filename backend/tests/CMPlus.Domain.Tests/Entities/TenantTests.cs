using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class TenantTests
{
    [Fact]
    public void New_Tenant_Defaults_To_Active()
    {
        var tenant = new Tenant("Acme Construction");

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Suspend_Then_Activate_Round_Trips()
    {
        var tenant = new Tenant("Acme Construction");

        tenant.Suspend();
        Assert.Equal(TenantStatus.Suspended, tenant.Status);

        tenant.Activate();
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Constructor_Rejects_Blank_Name()
    {
        Assert.Throws<DomainException>(() => new Tenant(""));
        Assert.Throws<DomainException>(() => new Tenant("   "));
    }
}
