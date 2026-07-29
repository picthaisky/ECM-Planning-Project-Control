using CMPlus.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.ErrorHandling;

/// <summary>
/// Last-resort exception -&gt; RFC 7807 <see cref="ProblemDetails"/> mapping (S2-BE-03). Registered
/// via <c>AddExceptionHandler&lt;GlobalExceptionHandler&gt;()</c> + <c>app.UseExceptionHandler()</c>
/// so every unhandled exception - a bug, an infrastructure failure, a <see cref="DomainException"/>
/// that somehow escaped a handler's own try/catch - produces a stable, client-safe response
/// instead of the framework's default developer exception page or a bare 500 with a stack trace.
/// <see cref="ValidationException"/> (thrown by <see cref="CMPlus.Application.Common.ValidationBehavior{TRequest,TResponse}"/>
/// only as a fallback for a non-<c>Result</c>-shaped request) is the one exception type mapped to
/// 400 here rather than 500 - everything else is treated as unexpected.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        var (status, type, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "validation-error", "One or more validation errors occurred."),
            DomainException => (StatusCodes.Status400BadRequest, "domain-rule-violation", "The request violates a domain rule."),
            _ => (StatusCodes.Status500InternalServerError, "unexpected-error", "An unexpected error occurred."),
        };

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Type = $"https://cmplus.dev/problems/{type}",
            Title = title,
            // Deliberately NOT exception.Message for the 500 case - never leak internal detail
            // (SQL text, stack traces, file paths) to the client (conventions.md: "API errors:
            // ProblemDetails only; never leak stack traces or SQL").
            Detail = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Please contact support if the problem persists."
                : exception.Message,
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
