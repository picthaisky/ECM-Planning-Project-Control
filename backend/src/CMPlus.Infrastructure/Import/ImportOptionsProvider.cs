using CMPlus.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Import;

public sealed class ImportOptionsProvider(IOptions<ImportOptions> options) : IImportOptionsProvider
{
    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;

    public long MaxDecompressedSizeBytes => options.Value.MaxDecompressedSizeBytes;

    public long MaxEntityCount => options.Value.MaxEntityCount;
}
