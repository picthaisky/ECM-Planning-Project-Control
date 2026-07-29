namespace CMPlus.Infrastructure.Parsers.Xer;

/// <summary>
/// One tab-delimited row from a <c>%R</c> record, keyed by the field names declared on the most
/// recent <c>%F</c> row for the enclosing <c>%T</c> table - i.e. a plain column-name -&gt; raw-text
/// lookup, exactly mirroring how P6 XER rows are shaped (docs/10 §6 S3-BE-01: "tab-delimited
/// record-type-tagged format").
/// </summary>
public sealed class XerRow
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public XerRow(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>Raw text for <paramref name="fieldName"/>, or <c>""</c> if the field was not
    /// present on this row (a short row, or a field XER omits when it is blank) - never throws, since
    /// XER routinely omits trailing blank fields.</summary>
    public string Get(string fieldName) => _values.TryGetValue(fieldName, out var value) ? value : string.Empty;

    public bool TryGet(string fieldName, out string value) => _values.TryGetValue(fieldName, out value!) && !string.IsNullOrEmpty(value);
}

/// <summary>All <c>%R</c> rows collected under one <c>%T</c> table section.</summary>
public sealed class XerTable
{
    public string Name { get; }

    public List<XerRow> Rows { get; } = [];

    public XerTable(string name) => Name = name;
}

/// <summary>
/// The whole parsed document: every <c>%T</c> table encountered, keyed by table name
/// (case-insensitive - P6 always emits upper-case names, e.g. <c>PROJWBS</c>, but this is
/// defensive). A table this parser never asked for (e.g. <c>CURRTYPE</c>, <c>CALENDAR</c>) is still
/// collected but simply never read by <see cref="XerScheduleParser"/> - unknown tables are not an
/// error (a real P6 export contains dozens of tables Sprint 3 has no use for yet).
/// </summary>
public sealed class XerDocument
{
    private readonly Dictionary<string, XerTable> _tables = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<XerRow> RowsOf(string tableName) =>
        _tables.TryGetValue(tableName, out var table) ? table.Rows : [];

    public bool HasTable(string tableName) => _tables.ContainsKey(tableName);

    internal XerTable GetOrAddTable(string tableName)
    {
        if (!_tables.TryGetValue(tableName, out var table))
        {
            table = new XerTable(tableName);
            _tables[tableName] = table;
        }

        return table;
    }
}
