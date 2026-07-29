using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.ExportProgressTemplate;

/// <summary>S3-BE-03, US-3.3 (export half): renders a blank progress-update Excel template listing
/// every activity in the project. The XLSX bytes are the "value" here - no separate DTO wrapper.</summary>
public sealed record ExportProgressTemplateQuery(Guid ProjectId) : IRequest<Result<byte[]>>;
