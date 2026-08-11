namespace CMPlus.WebApi.Middleware;

/// <summary>
/// Opts a controller action into <see cref="IdempotencyMiddleware"/> (S13-BE-01). Deliberately an
/// explicit, grep-able, per-action marker rather than a route/path-prefix guess in the middleware
/// itself - this task's brief scopes the DoD to "the site-module write endpoints", not every mutating
/// endpoint in the API, and a marker attribute is the only mechanism that says exactly which ones
/// without the middleware needing to know controller/route structure at all. See
/// <see cref="IdempotencyMiddleware"/>'s class remarks for the current, complete list of endpoints
/// carrying this attribute and why each one does.
///
/// <para>Has no effect unless the caller also sends the <c>Idempotency-Key</c> header - carrying this
/// attribute makes an action idempotency-<i>capable</i>, it does not make the header mandatory (the
/// DoD's own wording is "รองรับ" - support - not "require").</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute
{
}
