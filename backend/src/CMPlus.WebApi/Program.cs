using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using CMPlus.Application;
using CMPlus.Application.Abstractions;
using CMPlus.Infrastructure;
using CMPlus.WebApi.Auth;
using CMPlus.WebApi.ErrorHandling;
using CMPlus.WebApi.HealthChecks;
using CMPlus.WebApi.Json;
using CMPlus.WebApi.Middleware;
using CMPlus.WebApi.Networking;
using CMPlus.WebApi.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;

// Sprint 4 perf finding (docs/perf/baseline.md): a k6 run at 20 concurrent VUs starting
// instantly showed P95=108ms/P99=145ms/max=410ms against the WBS tree endpoint, breaching the
// <100ms target (docs/9. §1) - but a 1-VU run against the identical dataset measured
// P95=20.96ms, and the query handler itself measures P95=31.82ms against real SQL Server
// (database-engineer, S4-DB-01). No blocking call (`.Result`/`.Wait()`/`GetAwaiter().GetResult()`)
// exists anywhere in this solution's production code (verified by repo-wide search) - this is the
// well-documented .NET ThreadPool "cold start" behavior: the pool grows its worker-thread count
// gradually (by design, to avoid over-provisioning for load that never materializes) rather than
// immediately when concurrent async work suddenly arrives in a burst, so the first
// requests of a sudden concurrency spike queue briefly waiting for new threads to be injected.
// Real traffic arrives more gradually than k6's instant ramp-up, but a cold container restart
// (e.g. right after a deploy, or the first request after scale-out) can see exactly this pattern.
// SetMinThreads pre-warms the pool so it never needs to grow reactively under normal load;
// sized generously above the default (which equals ProcessorCount) since this API is I/O-bound
// (EF Core/SQL Server round trips), not CPU-bound, so many more threads than cores can be
// concurrently blocked waiting on I/O without any of them consuming actual CPU meanwhile.
ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Sprint 4 perf finding (docs/perf/baseline.md): the WBS tree endpoint's real HTTP-level P95
// intermittently breached the <100ms target (docs/9. §1, non-negotiable) even though the query
// handler itself measures ~32ms - the ~1 MB uncompressed JSON response was the prime suspect.
// EnableForHttps=true is a deliberate, considered choice, not the framework default (off by
// default since .NET Core 3 specifically over BREACH/CRIME risk - an attacker who can both
// observe response size AND inject content reflected into the same compressed response can
// infer secret bytes via compression-ratio side channel). None of this API's controllers ever
// echo caller-supplied input back into a body that also carries a secret (JWTs travel in the
// Authorization header/login response only, never mixed with reflected request content) - the
// classic BREACH shape doesn't exist on this surface today. Revisit this assumption before
// adding any endpoint that reflects request content (e.g. a search/filter echo) into a response.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // design.md §2: every decimal (money AND percent) is wire-serialized as a JSON string.
        options.JsonSerializerOptions.Converters.Add(new DecimalAsStringJsonConverter());
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Health checks (ADR-0010: /health/live + /health/ready from Phase 0).
// The database readiness check (untagged → runs on /health/ready, not /health/live) closes the gap
// S15-DO-02's cloud runbook flagged: without it /health/ready reported Healthy even with the DB down.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// S2-BE-03: ProblemDetails for every non-2xx MVC result (NotFound(), BadRequest(), the built-in
// 400 model-validation response, etc.) - not only for unhandled exceptions, which
// GlobalExceptionHandler covers separately below.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// S2-BE-04..07: EF Core DbContext + readers, seeders, audit interceptor (Infrastructure's
// composition root). S1-BE-04's progress reader is also registered here.
builder.Services.AddInfrastructure(builder.Configuration);

// S2-BE-01/04-07: MediatR handlers, FluentValidation validators, the pure approval routing
// engine (Application's composition root).
builder.Services.AddApplication();

// S2-BE-01/02: the real, JWT-claim-backed ITenantProvider/ICurrentUserContext replacing the
// design-time/seed-only stand-ins used before this sprint.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();
builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();

// S2-BE-01: JWT bearer authentication. The signing key comes exclusively from configuration/
// environment (Jwt__SigningKey, see infra/docker/.env.example) - never hardcoded
// (docs/security/secrets-policy.md). Options are configured lazily against the injected
// IConfiguration (rather than read eagerly off builder.Configuration here) so that
// WebApplicationFactory-based tests, which append their own configuration source *after* this
// point in the pipeline, can still override the signing key/issuer/audience - reading
// builder.Configuration directly at this point would freeze in the pre-test-override values.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey configuration (env var Jwt__SigningKey) is required and was not supplied.");
        var issuer = configuration["Jwt:Issuer"] ?? "CMPlus";
        var audience = configuration["Jwt:Audience"] ?? "CMPlusClients";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // Security review sprint-15.md L-02: the signing key is symmetric-only today (no
            // asymmetric key exists anywhere in this codebase), so classic alg-confusion
            // (RS256 public key mistaken for an HS256 secret) has no live surface yet - this is
            // defense-in-depth that pins the one algorithm actually issued (JwtTokenService uses
            // HmacSha256 exclusively) so a future asymmetric-key addition elsewhere in the pipeline
            // can never silently reopen that class of attack via this validator.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

        options.Events = new JwtBearerEvents
        {
            // S2-BE-01 DoD: expired/tampered token -> 401 + ProblemDetails, never a stack trace
            // or the framework's default bare/empty challenge response.
            OnChallenge = context =>
            {
                context.HandleResponse();

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Type = "https://cmplus.dev/problems/invalid-token",
                    Title = "Authentication is required, or the supplied token is invalid or expired.",
                    Instance = context.Request.Path,
                };

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                return context.Response.WriteAsJsonAsync(problem);
            },
        };
    });

// Security review sprint-15.md L-05: without an explicit FallbackPolicy, an endpoint added in the
// future without an [Authorize]/[AllowAnonymous] attribute of its own would default to anonymous
// (ASP.NET Core's own framework default) - the exact "forgot to lock the door" class of bug A01
// describes. Every one of today's 27 controllers is already explicitly [Authorize]'d (code-verified,
// sprint-15-owasp.md §1.2), so this is latent hardening, not a live fix - but it is the cheapest
// defense-in-depth available: a bare RequireAuthenticatedUser() policy applied to every endpoint that
// does not already carry its own authorization metadata. The two routes that must stay reachable
// without a token - /auth/login and both /health/* endpoints - carry an explicit AllowAnonymous
// below/on the controller so this fallback never breaks them.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Security review sprint-15-owasp.md M-1: rate-limit POST /api/v1/auth/login against online
// brute-force / credential-stuffing. Registers the ASP.NET Core rate-limiter services plus a
// GlobalLimiter chaining a per-IP and a per-account (submitted email) sliding window, scoped to the
// login route only - see LoginRateLimiterSetup for the numbers and the chained-limiter rationale.
builder.Services.AddLoginRateLimiting();

// sprint-16.md F-1: trust X-Forwarded-For/Proto only from the configured proxy network, so the per-IP
// login limiter above sees the real client IP behind the staging/prod reverse proxy — never trust-all
// (that would let clients spoof the header and defeat the limiter). Blank config trusts nothing.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ForwardedHeadersSetup.Configure(options, builder.Configuration["ForwardedHeaders:KnownNetwork"]));

var app = builder.Build();

// sprint-16.md F-1: apply forwarded headers FIRST, so the corrected client IP and scheme are in
// place before HTTPS redirection and the per-IP rate limiter read them. Trusts only the configured
// proxy network (see ForwardedHeadersSetup); a no-op when unconfigured.
app.UseForwardedHeaders();

// sprint-10.md L-06 / sprint-16.md S16-SEC-01: baseline security headers on EVERY response.
// Outermost so its OnStarting callback is attached before any downstream path (including
// UseExceptionHandler's error response and the rate limiter's 429) can produce bytes.
app.UseMiddleware<SecurityHeadersMiddleware>();

// S2-BE-03: catches everything not already handled below (bugs, infrastructure failures) and
// renders a stable ProblemDetails body - registered first so it wraps the whole pipeline.
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // sprint-16.md S16-SEC-01: instruct browsers to pin HTTPS for future visits (production only -
    // never in Development, where the app is reached over plain-HTTP localhost and an HSTS pin would
    // wrongly persist in the developer's browser). Pairs with UseHttpsRedirection below.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Must run before any middleware that writes the response body it should compress.
app.UseResponseCompression();

app.UseAuthentication();
app.UseAuthorization();

// Security review sprint-15-owasp.md M-1: the per-account limiter partition-key delegate is
// synchronous and cannot read the request body, so this middleware peeks + rewinds the login body to
// stash the normalized email in HttpContext.Items *before* UseRateLimiter() reads it. It is a cheap
// no-op for every non-login request. Must precede UseRateLimiter().
app.UseMiddleware<LoginEmailCaptureMiddleware>();
app.UseRateLimiter();

// S13-BE-01 (ADR-0005 US-13.1): Idempotency-Key support on the site-module write endpoints
// ([Idempotent]-attributed actions only - see IdempotencyMiddleware's class remarks). Must run
// AFTER UseAuthentication/UseAuthorization (needs the resolved tenant/user claims, and a request
// that never authorizes must never reach - let alone reserve - a key) and AFTER
// UseResponseCompression (so compression sees this middleware's final, restored plain bytes and
// compresses them exactly as it would without this middleware, rather than compressing into, or
// being bypassed by, its internal response buffer). Routing has already resolved the endpoint by
// this point - no explicit UseRouting() call exists anywhere in this pipeline, so ASP.NET Core
// inserts it implicitly at the very start (the same reason UseAuthorization, above, can already
// see each action's [Authorize] metadata) - so `context.GetEndpoint()` reliably sees [Idempotent] too.
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();

// Liveness: process is up and serving requests - never runs dependency checks, so a DB
// outage must not flip this and trigger an orchestrator restart loop.
// AllowAnonymous (L-05): an orchestrator/load-balancer probe carries no bearer token - without this,
// the FallbackPolicy above would 401 every liveness/readiness check.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Readiness: runs every registered health check. No dependency checks are registered directly in
// WebApi (a DbContext health check would force an EF Core assembly reference here, which
// CMPlus.Architecture.Tests explicitly forbids) - Infrastructure remains the only place that
// talks to EF Core.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true }).AllowAnonymous();

app.Run();

// Makes the top-level-statements-generated Program class accessible to
// WebApplicationFactory<Program> from the test assemblies (Microsoft.AspNetCore.Mvc.Testing
// requires the entry point type to be visible outside this assembly).
public partial class Program
{
}
