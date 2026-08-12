using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CMPlus.WebApi.RateLimiting;

/// <summary>
/// Security review sprint-15.md M-1: <c>POST /api/v1/auth/login</c> rate limiting. See
/// <see cref="LoginRateLimitOptions"/> for the numbers and the reasoning behind them.
///
/// <para><b>Why a <c>GlobalLimiter</c>, not a named <c>[EnableRateLimiting]</c> policy.</b> ASP.NET
/// Core's named-policy API (<c>RateLimiterOptions.AddPolicy</c>) resolves exactly one
/// <c>RateLimitPartition</c> per policy per request - it has no built-in way to combine two
/// independently-keyed limiters (IP AND email) into a single endpoint's policy. <c>GlobalLimiter</c>
/// accepts a <see cref="PartitionedRateLimiter{HttpContext}"/> directly, and
/// <see cref="PartitionedRateLimiter.CreateChained{TResource}"/> is the framework's own supported way
/// to AND multiple partitioned limiters together (a request must pass every chained limiter). Scoping
/// this to only the login route - despite "global" in the name - is done inside each limiter's own
/// partition-key delegate: every other route resolves to <see cref="RateLimitPartition.GetNoLimiter{TKey}"/>,
/// a shared, stateless, always-permits partition, so no other endpoint pays any cost from this
/// registration.</para>
/// </summary>
public static class LoginRateLimiterSetup
{
    public const string LoginPath = "/api/v1/auth/login";

    private const string RateLimitExceededProblemType = "https://cmplus.dev/problems/rate-limit-exceeded";

    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        // Bound lazily against the injected IConfiguration (not builder.Configuration eagerly) -
        // same reasoning as JwtBearerOptions' setup in Program.cs: WebApplicationFactory-based tests
        // append their own configuration source *after* this point in the pipeline, and only a lazy
        // read sees that override (CustomWebApplicationFactory sets RateLimiting:Login:Enabled=false).
        services.AddOptions<LoginRateLimitOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(LoginRateLimitOptions.SectionName).Bind(options))
            .Validate(o => o.PermitLimitPerIp > 0, "RateLimiting:Login:PermitLimitPerIp must be positive.")
            .Validate(o => o.WindowSecondsPerIp > 0, "RateLimiting:Login:WindowSecondsPerIp must be positive.")
            .Validate(o => o.SegmentsPerWindowPerIp > 0, "RateLimiting:Login:SegmentsPerWindowPerIp must be positive.")
            .Validate(o => o.PermitLimitPerAccount > 0, "RateLimiting:Login:PermitLimitPerAccount must be positive.")
            .Validate(o => o.WindowSecondsPerAccount > 0, "RateLimiting:Login:WindowSecondsPerAccount must be positive.")
            .Validate(o => o.SegmentsPerWindowPerAccount > 0, "RateLimiting:Login:SegmentsPerWindowPerAccount must be positive.")
            .ValidateOnStart();

        // Registers the services UseRateLimiter()/the middleware need; the real policy is configured
        // immediately below, lazily, against the just-registered IOptions<LoginRateLimitOptions>.
        services.AddRateLimiter(_ => { });

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<LoginRateLimitOptions>>((rateLimiterOptions, loginRateLimitOptionsAccessor) =>
            {
                var loginOptions = loginRateLimitOptionsAccessor.Value;

                rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    BuildPerIpLimiter(loginOptions),
                    BuildPerAccountLimiter(loginOptions));

                rateLimiterOptions.OnRejected = WriteRejectionAsync;
            });

        return services;
    }

    private static PartitionedRateLimiter<HttpContext> BuildPerIpLimiter(LoginRateLimitOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!ShouldLimit(context, options))
            {
                return RateLimitPartition.GetNoLimiter("non-login");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
            return RateLimitPartition.GetSlidingWindowLimiter(
                $"ip:{ip}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimitPerIp,
                    Window = TimeSpan.FromSeconds(options.WindowSecondsPerIp),
                    SegmentsPerWindow = options.SegmentsPerWindowPerIp,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

    private static PartitionedRateLimiter<HttpContext> BuildPerAccountLimiter(LoginRateLimitOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!ShouldLimit(context, options))
            {
                return RateLimitPartition.GetNoLimiter("non-login");
            }

            var email = context.Items.TryGetValue(LoginEmailCaptureMiddleware.ItemsKey, out var value) && value is string normalizedEmail
                ? normalizedEmail
                : "unknown-email";

            return RateLimitPartition.GetSlidingWindowLimiter(
                $"account:{email}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimitPerAccount,
                    Window = TimeSpan.FromSeconds(options.WindowSecondsPerAccount),
                    SegmentsPerWindow = options.SegmentsPerWindowPerAccount,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

    private static bool ShouldLimit(HttpContext context, LoginRateLimitOptions options) =>
        options.Enabled
        && HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>DoD: 429 carries a <c>Retry-After</c> header and a ProblemDetails body, matching the
    /// <c>OnChallenge</c>/<c>IdempotencyMiddleware</c> convention already established in this codebase
    /// - never a bare status code.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var reportedRetryAfter)
            ? reportedRetryAfter
            : TimeSpan.FromSeconds(60); // Defensive fallback - every limiter type used above reports this metadata.

        context.HttpContext.Response.Headers["Retry-After"] =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Type = RateLimitExceededProblemType,
            Title = "Too many login attempts. Please wait before trying again.",
            Instance = context.HttpContext.Request.Path,
        };

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        // Pass the content type to WriteAsJsonAsync explicitly: the overload without it resets the
        // response Content-Type to "application/json", silently undoing a prior assignment - so the
        // ProblemDetails convention (application/problem+json, matching OnChallenge/IdempotencyMiddleware)
        // only holds when it is passed here. Caught by LoginRateLimiterTests.
        await context.HttpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
