using CMPlus.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Photos;

public sealed class PhotoOptionsProvider(IOptions<PhotoOptions> options) : IPhotoOptionsProvider
{
    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;
}
