using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CMPlus.WebApi.Json;

/// <summary>
/// Wire-format rule from docs/specs/master-plan/design.md §2 ("money as decimal-safe strings")
/// and its own JSON samples throughout (e.g. <c>"minAmount": "0.00"</c>,
/// <c>"cumulativeVoEscalationPct": "10.00"</c>) - every <c>decimal</c>/<c>decimal?</c> value
/// (money AND percent alike) is serialized as a JSON *string*, never a bare number, so a client
/// can never lose precision by round-tripping through a JS/JSON-number float. Applied globally via
/// <c>AddJsonOptions</c> so no DTO needs a per-property attribute to get this right.
/// </summary>
public sealed class DecimalAsStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        // decimal.ToString() preserves the value's own scale (e.g. 500000.00m -> "500000.00",
        // 1.166667m -> "1.166667") - no fixed format string, so this converter works unchanged
        // for every decimal(p,s) shape used across the API (money 18,2 / percent 5,2 / PF 9,4/9,6).
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
