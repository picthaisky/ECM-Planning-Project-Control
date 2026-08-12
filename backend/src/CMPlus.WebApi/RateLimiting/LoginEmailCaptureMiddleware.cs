using System.Text.Json;

namespace CMPlus.WebApi.RateLimiting;

/// <summary>
/// Security review sprint-15.md M-1: the per-account dimension of the login rate limiter
/// (<see cref="LoginRateLimiterSetup"/>) needs the submitted email, but a
/// <c>PartitionedRateLimiter</c> partition-key delegate is synchronous and runs against
/// <see cref="HttpContext"/> alone - it cannot itself read/parse the request body. This middleware
/// runs earlier in the pipeline (registered before <c>UseRateLimiter()</c> in <c>Program.cs</c>),
/// peeks the login route's small JSON body for an <c>email</c> property, stores the normalized value
/// in <see cref="HttpContext.Items"/> under <see cref="ItemsKey"/>, and rewinds the body stream so
/// the real <c>[FromBody] LoginRequest</c> model binder downstream still sees the full, unconsumed
/// body. Normalization (<c>Trim().ToLowerInvariant()</c>) matches <c>LoginCommandHandler</c>'s own
/// email normalization exactly, so the same account maps to the same rate-limit partition regardless
/// of casing/whitespace.
///
/// <para><b>Every non-login request, and any request too large to be a plausible login body, is a
/// cheap no-op</b> (method+path string check first; <see cref="MaxPeekBodyBytes"/> bounds how much
/// this middleware will ever buffer, so an oversized/chunked body posted to the login route cannot
/// be turned into an unbounded buffering cost here - it is left with no captured email, which falls
/// back to a shared "unknown-email" partition in <see cref="LoginRateLimiterSetup"/>; the per-IP
/// limiter still applies as the backstop, and <c>LoginCommandValidator</c>/model binding reject the
/// malformed/oversized request on their own merits further down the pipeline).</para>
///
/// <para><b>Never throws on malformed JSON</b> - a request an attacker deliberately malforms to
/// dodge the per-account limiter still lands in the "unknown-email" partition, not an unhandled
/// exception.</para>
/// </summary>
public sealed class LoginEmailCaptureMiddleware(RequestDelegate next)
{
    public const string ItemsKey = "cmplus:login-rate-limit-email";

    /// <summary>A real login body is `{"email":"...","password":"..."}` - a few hundred bytes at
    /// most. 8 KiB is generous headroom while still bounding the buffering cost described in this
    /// type's class remarks.</summary>
    private const int MaxPeekBodyBytes = 8 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldPeek(context.Request))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && TryGetEmailPropertyCaseInsensitive(document.RootElement, out var email))
            {
                context.Items[ItemsKey] = email;
            }
        }
        catch (JsonException)
        {
            // Malformed body - leave ItemsKey unset. See this type's class remarks.
        }
        finally
        {
            context.Request.Body.Position = 0;
        }

        await next(context);
    }

    private static bool ShouldPeek(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals(LoginRateLimiterSetup.LoginPath, StringComparison.OrdinalIgnoreCase)
        && request.ContentType is { } contentType
        && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
        && request.ContentLength is > 0 and <= MaxPeekBodyBytes;

    private static bool TryGetEmailPropertyCaseInsensitive(JsonElement root, out string normalizedEmail)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "email", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                normalizedEmail = raw.Trim().ToLowerInvariant();
                return true;
            }
        }

        normalizedEmail = string.Empty;
        return false;
    }
}
