namespace CMPlus.Domain.Common;

/// <summary>
/// Raised from within a Domain entity/value object when an invariant is violated (e.g. a WBS
/// cycle, a percentage outside [0,100]). Distinct from unexpected/infrastructure exceptions -
/// Application handlers catch this at the boundary and translate it into a
/// <see cref="Result"/>/<see cref="Result{T}"/> failure (never a raw 500 with a stack trace, per
/// .claude/knowledge/patterns/conventions.md).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
