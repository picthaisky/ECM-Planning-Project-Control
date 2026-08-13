using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace CMPlus.WebApi.Networking;

/// <summary>
/// sprint-16.md F-1: the API runs behind a TLS-terminating reverse proxy in staging/production (see
/// infra/staging/docker-compose.yml). Without forwarded-header handling, the per-IP login rate
/// limiter (sprint-15-owasp.md M-1) partitions on the proxy's IP, so every client shares one bucket
/// and the per-IP brute-force protection is defeated.
/// <para>
/// This configures <see cref="ForwardedHeadersOptions"/> to honour <c>X-Forwarded-For</c>/
/// <c>X-Forwarded-Proto</c> <b>only</b> from the explicitly configured proxy network(s)
/// (<c>ForwardedHeaders:KnownNetwork</c>, a comma-separated CIDR list). A blank/unset value trusts
/// <b>nothing</b> — the framework's default known networks are cleared, so the API falls back to the
/// direct peer IP. Behind a proxy that means the limiter degrades to per-proxy (all clients share the
/// proxy IP) but is <b>not</b> spoofable — the safe failure direction. A trust-all configuration is
/// deliberately impossible here: it would let any client set <c>X-Forwarded-For</c> and evade the
/// limiter entirely, which is worse than the gap this closes.
/// </para>
/// </summary>
public static class ForwardedHeadersSetup
{
    public static void Configure(ForwardedHeadersOptions options, string? knownNetworks)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;           // exactly one proxy hop (the staging/prod reverse proxy)
        options.KnownIPNetworks.Clear();    // drop the framework's loopback defaults — trust only what is configured
        options.KnownProxies.Clear();       // never a bare trust-by-IP list here

        if (string.IsNullOrWhiteSpace(knownNetworks))
        {
            return;
        }

        foreach (var entry in knownNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!entry.Contains('/'))
            {
                throw new FormatException($"ForwardedHeaders:KnownNetwork entry '{entry}' must be CIDR (e.g. 172.18.0.0/16).");
            }

            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
        }
    }
}
