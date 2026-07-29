namespace CMPlus.Domain.Enums;

/// <summary>Lifecycle of a <c>FileImportJob</c> (docs/10 §3, S3-BE-04). A job is created
/// <see cref="Pending"/> and transitions exactly once to a terminal state - never back.</summary>
public enum ImportJobStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}
