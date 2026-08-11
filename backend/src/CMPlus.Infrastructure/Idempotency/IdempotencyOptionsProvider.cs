using CMPlus.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Idempotency;

public sealed class IdempotencyOptionsProvider(IOptions<IdempotencyOptions> options) : IIdempotencyOptionsProvider
{
    public int MaxReplayableResponseBodyBytes => options.Value.MaxReplayableResponseBodyBytes;
}
