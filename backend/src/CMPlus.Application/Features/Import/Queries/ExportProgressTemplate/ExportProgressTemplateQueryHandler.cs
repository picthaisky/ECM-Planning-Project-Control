using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Import.Queries.ExportProgressTemplate;

public sealed class ExportProgressTemplateQueryHandler(
    IImportRepository repository, IExcelProgressTemplateWriter templateWriter)
    : IRequestHandler<ExportProgressTemplateQuery, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(ExportProgressTemplateQuery request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<byte[]>.Failure(ImportErrorCodes.ProjectNotFound);
        }

        var rows = await repository.GetActivityTemplateRowsAsync(request.ProjectId, cancellationToken);
        var bytes = templateWriter.WriteTemplate(rows);

        return Result<byte[]>.Success(bytes);
    }
}
