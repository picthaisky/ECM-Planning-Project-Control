using Microsoft.AspNetCore.Http;

namespace CMPlus.WebApi.Middleware;

/// <summary>
/// sprint-10.md L-06 / sprint-16.md S16-SEC-01: adds the baseline security response headers to
/// <b>every</b> response - API JSON, a ProblemDetails error, a short-circuiting 429, and the health
/// probes alike. Registered as the <b>outermost</b> app middleware and applied inside
/// <see cref="HttpResponse.OnStarting"/> so the headers are present no matter which downstream path
/// actually produces the bytes - including <c>UseExceptionHandler</c>'s re-executed error response and
/// the rate limiter's rejection, both of which can short-circuit before a controller ever runs.
/// <para>
/// Headers are written with indexer assignment, <b>never</b> <c>Headers.Append</c>: if something
/// downstream already set the same header (e.g. <c>ProjectPhotosController.Get</c>'s own
/// <c>X-Content-Type-Options: nosniff</c>, which is deliberately set with an indexer for exactly this
/// reason - see <c>ProjectPhotosControllerHeaderTests</c>) the value is overwritten with the identical
/// value, never doubled into <c>"nosniff,nosniff"</c>.
/// </para>
/// <para>
/// Deliberately <b>not</b> set here because they are topology-dependent (see sprint-16.md findings):
/// HSTS (wired as <c>UseHsts</c> in <c>Program.cs</c>, production only), CORS, and a full
/// <c>Content-Security-Policy</c> (needs the web app's real origin). Framing is covered for this
/// origin by <c>X-Frame-Options: DENY</c> below; this API is served on its own origin and returns no
/// first-party HTML to frame.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Register the header-writer before invoking the rest of the pipeline, so it is already
        // attached to this response if anything downstream short-circuits or throws.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            // sprint-16.md F-2: this API serves only JSON (the SPA is served by the separate `web`
            // nginx container, never this origin), so the tightest possible policy is both safe and
            // correct — a response that is ever rendered directly in a browser can load nothing and
            // frame nothing. If first-party HTML is ever served here, this must be relaxed for it.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
