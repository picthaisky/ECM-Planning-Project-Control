namespace CMPlus.WebApi.RateLimiting;

/// <summary>
/// Bound from the <c>RateLimiting:Login</c> configuration section (security review
/// sprint-15-owasp.md M-1) - same "read the cap from config, don't hardcode" discipline as
/// <c>PhotoOptions</c>/<c>ImportOptions</c>/<c>IdempotencyOptions</c>.
///
/// <para><b>Two independent dimensions, both enforced (<see cref="LoginRateLimiterSetup"/>
/// chains them via <c>PartitionedRateLimiter.CreateChained</c>).</b> Per-IP is the baseline every
/// public login endpoint needs; per-account (keyed on the submitted, normalized email) is added
/// because credential-stuffing tooling routinely rotates source IPs across a large botnet/proxy
/// pool while reusing the same small list of target email/password pairs - a pure per-IP limiter
/// is trivially bypassed by that shape of attack, but an attacker cannot rotate the one thing they
/// are actually trying to guess.</para>
///
/// <para><b>Numbers, and why.</b> <see cref="PermitLimitPerIp"/>=10 per <see cref="WindowSecondsPerIp"/>=60s:
/// generous enough that a shared-NAT office/campus network or a user who mistypes their password a
/// few times is never affected, while still capping a single source to at most 10 login attempts a
/// minute - already operationally useless against PBKDF2-HMAC-SHA256 @210k-iteration hashes (Pbkdf2PasswordHasher),
/// which cost real CPU time per guess even before this limiter exists.
/// <see cref="PermitLimitPerAccount"/>=5 per <see cref="WindowSecondsPerAccount"/>=300s (5 minutes):
/// deliberately tighter than the IP limit, because repeated failures against ONE specific account is
/// a much stronger attack signal than aggregate traffic from one IP - capping it to ~1 attempt/minute
/// sustained makes online guessing against a single targeted account impractically slow. This is a
/// self-resetting sliding-window throttle, not a growing or permanent lockout - deliberately, since a
/// hard account lockout keyed on the submitted email is itself an unauthenticated-attacker-triggerable
/// denial-of-service against the real account holder (OWASP's own caution against lockout-based
/// defenses); the bounded "wait a few minutes" cost here is the accepted trade-off.</para>
///
/// <para><b>Both windows use a <i>sliding</i> window with several segments</b> (not a fixed window)
/// so an attacker cannot double their effective rate by timing a burst across a fixed-window
/// boundary (10 requests just before the boundary + 10 just after would otherwise permit 20 in a
/// few seconds).</para>
///
/// <para><b>Test-suite stability (<see cref="Enabled"/>).</b> <c>CustomWebApplicationFactory</c>
/// (the shared <c>IClassFixture</c> most of the 569 integration tests share) sets
/// <c>RateLimiting:Login:Enabled=false</c> so the limiter is a structural no-op for that shared
/// factory - several existing test classes call <c>/auth/login</c> repeatedly against the SAME
/// factory instance and would otherwise start 429-ing unpredictably depending on run order/timing.
/// <c>LoginRateLimiterTests</c> is the one place this is exercised with the limiter genuinely turned
/// on, using its own dedicated, tiny-limit factory instance - never the shared one.</para>
/// </summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";

    /// <summary>Master switch. Defaults to <see langword="true"/> (the safe default for a real
    /// deployment); <c>CustomWebApplicationFactory</c> is the one place this is overridden to
    /// <see langword="false"/> - see this type's class remarks.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max login attempts a single source IP may make within <see cref="WindowSecondsPerIp"/>.</summary>
    public int PermitLimitPerIp { get; set; } = 10;

    /// <summary>The per-IP sliding window's total length, in seconds.</summary>
    public int WindowSecondsPerIp { get; set; } = 60;

    /// <summary>How many segments the per-IP sliding window is divided into - more segments means a
    /// smoother (less bursty-at-the-boundary) rate, at the cost of a little more per-partition state.</summary>
    public int SegmentsPerWindowPerIp { get; set; } = 4;

    /// <summary>Max login attempts against a single submitted (normalized) email within
    /// <see cref="WindowSecondsPerAccount"/> - regardless of which IP(s) they arrive from.</summary>
    public int PermitLimitPerAccount { get; set; } = 5;

    /// <summary>The per-account sliding window's total length, in seconds.</summary>
    public int WindowSecondsPerAccount { get; set; } = 300;

    /// <summary>How many segments the per-account sliding window is divided into.</summary>
    public int SegmentsPerWindowPerAccount { get; set; } = 5;
}
