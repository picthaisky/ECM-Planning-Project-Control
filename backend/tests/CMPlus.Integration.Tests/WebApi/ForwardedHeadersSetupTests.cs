using CMPlus.WebApi.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// sprint-16.md F-1: the API must honour X-Forwarded-For ONLY from the configured proxy network, so
/// the per-IP login rate limiter sees the real client IP without letting anyone spoof it. These tests
/// pin the two security-critical properties: (1) trust is limited to exactly the configured network(s)
/// and (2) it is never trust-all, and the safe default (no config → trust nothing) holds.
/// </summary>
public class ForwardedHeadersSetupTests
{
    [Fact]
    public void Configure_Trusts_Only_The_Single_Configured_Network()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, "172.18.0.0/16");

        // XForwardedFor is honoured (so the limiter can read the real client IP)...
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        // ...but ONLY from the one configured network, and by network — never a bare proxy IP list.
        var net = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(16, net.PrefixLength);
        Assert.Equal("172.18.0.0", net.BaseAddress.ToString());
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void Configure_Accepts_Multiple_Comma_Separated_Networks()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, "172.18.0.0/16, 10.0.0.0/8");

        Assert.Equal(2, options.KnownIPNetworks.Count);
        Assert.Contains(options.KnownIPNetworks, n => n.PrefixLength == 8 && n.BaseAddress.ToString() == "10.0.0.0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_With_No_Network_Trusts_Nothing(string? config)
    {
        // Safe default: the framework's loopback defaults are cleared and nothing is added, so no
        // forwarded header is trusted from anywhere — degraded (per-proxy) but not spoofable.
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, config);

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void Configure_Rejects_A_Non_Cidr_Entry_Rather_Than_Silently_Trusting_A_Bare_Ip()
    {
        // A bare IP without a prefix length is a config error — fail loudly, never guess a mask.
        var options = new ForwardedHeadersOptions();

        Assert.Throws<FormatException>(() => ForwardedHeadersSetup.Configure(options, "172.18.0.5"));
    }
}
