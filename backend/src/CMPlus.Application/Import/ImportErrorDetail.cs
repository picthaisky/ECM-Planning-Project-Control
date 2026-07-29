using System.Text.Encodings.Web;
using System.Text.Json;

namespace CMPlus.Application.Import;

/// <summary>
/// Convention shared by every import parser/handler: a <see cref="CMPlus.Domain.Common.Result{T}"/>
/// failure string is always <c>"{code}: {detail}"</c>, where <c>code</c> is one of
/// <see cref="ImportErrorCodes"/>'s constants - e.g. the XER parser reports a rejected relation
/// cycle as <c>"ImportRelationCycleDetected: A-1010 -&gt; A-1020 -&gt; A-1030 -&gt; A-1010"</c>.
/// <see cref="FromResultError"/> splits that back into a structured record for
/// <see cref="FileImportJob"/>'s <c>ErrorJson</c> column and, on the WebApi side,
/// <c>ResultProblemMapper</c> keys off just the <c>code</c> prefix.
/// </summary>
public sealed record ImportErrorDetail(string Code, string Detail)
{
    // Relaxed encoder: this JSON is our own diagnostic payload (FileImportJob.ErrorJson), never
    // rendered as HTML, so the default HTML-safe escaping of '>'/'<' (which would otherwise mangle
    // a relation-cycle chain like "A1010 -> A1020 -> A1010" into ">" escapes) is unnecessary.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ImportErrorDetail FromResultError(string resultError)
    {
        var separatorIndex = resultError.IndexOf(':');
        return separatorIndex > 0
            ? new ImportErrorDetail(resultError[..separatorIndex], resultError[(separatorIndex + 1)..].Trim())
            : new ImportErrorDetail(resultError, resultError);
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}
