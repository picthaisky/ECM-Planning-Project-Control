using System.Security.Cryptography;
using System.Text;

namespace CMPlus.WebApi.Middleware;

/// <summary>
/// Computes what "the same payload" means for <see cref="IdempotencyMiddleware"/> (S13-BE-01 design
/// note). Method and path are always folded in first, so a key accidentally reused across two
/// different endpoints is treated as a mismatch rather than an accidental collision.
///
/// <para><b>Two different strategies by content type, deliberately.</b> An ordinary JSON body (the
/// weather-log and batch-progress endpoints) is small, so the whole raw body is buffered and hashed -
/// cheap, and byte-exact. A multipart body (the photo upload) is not: re-hashing a multi-megabyte
/// file on every request - including a replay, which is exactly the case this feature exists to make
/// cheap and safe - would be real, avoidable cost. For <c>multipart/form-data</c> this instead
/// fingerprints the request's <i>shape</i> - every non-file field's value, plus each file part's
/// field name, original filename, declared Content-Type and length - and never touches a file part's
/// own bytes. <see cref="HttpRequest.ReadFormAsync"/> still has to fully parse the multipart body for
/// the request to be processed at all (ASP.NET Core's own <c>[FromForm]</c>/<c>IFormFile</c> model
/// binding calls exactly this method); calling it here does not add a second parse, it only moves the
/// same one earlier, and the parsed result is cached on the request so MVC's own binder reuses it
/// without re-reading anything.</para>
///
/// <para><b>Known, accepted limitation of the multipart fingerprint</b> (stated plainly, not
/// discovered later): two different files with the same field name, filename, Content-Type and byte
/// length are indistinguishable by this fingerprint, so a same-key retry that swapped in a different
/// file of identical shape would be treated as a replay rather than a mismatch. In this feature's
/// actual threat model - a lost-response retry of the caller's own prior request, never a different
/// caller or a different tenant - that shape is the overwhelmingly common "genuinely the same upload"
/// case, not an attacker substituting content; a full-byte comparison would close this at the cost of
/// re-hashing every photo on every request, which is precisely the cost this design avoids.</para>
/// </summary>
internal static class IdempotencyFingerprint
{
    public static async Task<string> ComputeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(request.Method).Append('\n');
        builder.Append(request.Path.Value).Append('\n');

        if (request.HasFormContentType)
        {
            await AppendFormShapeAsync(request, builder, cancellationToken);
        }
        else
        {
            await AppendRawBodyAsync(request, builder, cancellationToken);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task AppendFormShapeAsync(HttpRequest request, StringBuilder builder, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);

        foreach (var fieldName in form.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (var value in form[fieldName].OrderBy(v => v, StringComparer.Ordinal))
            {
                builder.Append("field:").Append(fieldName).Append('=').Append(value).Append('\n');
            }
        }

        foreach (var file in form.Files
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ThenBy(f => f.FileName, StringComparer.Ordinal))
        {
            builder.Append("file:").Append(file.Name).Append(':').Append(file.FileName).Append(':')
                   .Append(file.ContentType).Append(':').Append(file.Length).Append('\n');
        }
    }

    private static async Task AppendRawBodyAsync(HttpRequest request, StringBuilder builder, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(
            request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);

        // Rewind so the MVC model binder (which reads this same buffered body next) sees it from
        // the start - EnableBuffering() is what makes Position settable at all here.
        request.Body.Position = 0;

        builder.Append(body);
    }
}
