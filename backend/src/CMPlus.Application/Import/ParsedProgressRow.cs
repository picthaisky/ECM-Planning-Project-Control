namespace CMPlus.Application.Import;

/// <summary>
/// One data row from an imported progress-update Excel template (S3-BE-03, US-3.3), already
/// type-parsed and formula-injection-safe (any cell whose raw text began with <c>=</c>, <c>+</c>,
/// <c>-</c> or <c>@</c> was read as literal text, never evaluated). Resolving
/// <see cref="ActivityId"/> against the target project/tenant and turning this into an
/// <c>ActivityProgressLog</c> entry via <c>Activity.RecordProgress</c> is the Application command
/// handler's job, not the parser's - the parser has no DB access (ADR-0001).
/// </summary>
public sealed record ParsedProgressRow(
    Guid ActivityId,
    string ActivityCode,
    DateTimeOffset PeriodEndDate,
    decimal ProgressPercentage,
    decimal? ActualQuantity,
    int SourceRowNumber);

/// <summary>One row of the blank/prefilled template <see cref="Abstractions.IExcelProgressTemplateWriter"/>
/// produces for export (S3-BE-03) - the current state of one activity, offered back to the field
/// team to fill in updated values for.</summary>
public sealed record ActivityProgressTemplateRow(
    Guid ActivityId,
    string ActivityCode,
    string ActivityName,
    decimal CurrentProgressPercentage);
