using CMPlus.Application.Abstractions;

namespace CMPlus.Infrastructure;

/// <summary>Production <see cref="IDateTimeProvider"/> - the first real registration of this
/// abstraction (previously only test doubles used it). Needed from S2-BE-01/02 onward for JWT
/// expiry and audit timestamps.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
