using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using CMPlus.Application;
using CMPlus.Application.Abstractions;
using CMPlus.Infrastructure;
using CMPlus.WebApi.Auth;
using CMPlus.WebApi.ErrorHandling;
using CMPlus.WebApi.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
builder.Services.AddHealthChecks();

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

builder.Services.AddAuthorization();

var app = builder.Build();

// S2-BE-03: catches everything not already handled below (bugs, infrastructure failures) and
// renders a stable ProblemDetails body - registered first so it wraps the whole pipeline.
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Must run before any middleware that writes the response body it should compress.
app.UseResponseCompression();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness: process is up and serving requests - never runs dependency checks, so a DB
// outage must not flip this and trigger an orchestrator restart loop.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: runs every registered health check. No dependency checks are registered directly in
// WebApi (a DbContext health check would force an EF Core assembly reference here, which
// CMPlus.Architecture.Tests explicitly forbids) - Infrastructure remains the only place that
// talks to EF Core.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

app.Run();

// Makes the top-level-statements-generated Program class accessible to
// WebApplicationFactory<Program> from the test assemblies (Microsoft.AspNetCore.Mvc.Testing
// requires the entry point type to be visible outside this assembly).
public partial class Program
{
}
