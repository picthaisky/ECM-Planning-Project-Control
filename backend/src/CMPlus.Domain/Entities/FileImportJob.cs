using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// History record for one file-import attempt (XER/MSPDI/Excel), per docs/10 §2.5(e)/§3 and
/// US-3.4*. Unlike the append-only logs elsewhere in the domain, a job has a small mutable
/// lifecycle (<see cref="ImportJobStatus.Pending"/> -&gt; exactly one terminal state) so status,
/// row count and error detail can be queried after the fact (S3-BE-04 DoD: "history, not
/// fire-and-forget") - it is not itself a corrections-only append log.
/// </summary>
public sealed class FileImportJob : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public ImportFileFormat Format { get; private set; }

    public ImportJobStatus Status { get; private set; }

    /// <summary>Total rows created (WBS nodes + activities + relations, or progress-log rows)
    /// once <see cref="Status"/> is <see cref="ImportJobStatus.Succeeded"/>; 0 until then.</summary>
    public int RowsImported { get; private set; }

    /// <summary>Serialized error detail (error code + message, and for a rejected XER relation
    /// cycle, the offending chain) once <see cref="Status"/> is <see cref="ImportJobStatus.Failed"/>;
    /// null otherwise.</summary>
    public string? ErrorJson { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private FileImportJob()
    {
    }

    public FileImportJob(
        Guid tenantId,
        Guid projectId,
        string fileName,
        ImportFileFormat format,
        Guid createdByUserId,
        DateTimeOffset startedAt)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        FileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new DomainException("FileImportJob.FileName is required.")
            : fileName.Trim();
        Format = format;
        CreatedByUserId = createdByUserId;
        StartedAt = startedAt;
        Status = ImportJobStatus.Pending;
    }

    /// <summary>Terminal transition for a successful import. Idempotent-guarded: a job may only
    /// leave <see cref="ImportJobStatus.Pending"/> once (a job's outcome is never rewritten).</summary>
    public void MarkSucceeded(int rowsImported, DateTimeOffset finishedAt)
    {
        EnsurePending();

        if (rowsImported < 0)
        {
            throw new DomainException("RowsImported cannot be negative.");
        }

        Status = ImportJobStatus.Succeeded;
        RowsImported = rowsImported;
        FinishedAt = finishedAt;
    }

    /// <summary>Terminal transition for a rejected/failed import (size cap, malformed file, relation
    /// cycle, XXE attempt, etc). <paramref name="errorJson"/> carries the specific, user-facing
    /// reason - never a raw stack trace (conventions.md).</summary>
    public void MarkFailed(string errorJson, DateTimeOffset finishedAt)
    {
        EnsurePending();

        Status = ImportJobStatus.Failed;
        ErrorJson = string.IsNullOrWhiteSpace(errorJson)
            ? throw new DomainException("FileImportJob.ErrorJson is required for a failed job.")
            : errorJson;
        FinishedAt = finishedAt;
    }

    private void EnsurePending()
    {
        if (Status != ImportJobStatus.Pending)
        {
            throw new DomainException(
                $"FileImportJob '{Id}' has already reached a terminal state ({Status}) and cannot be transitioned again.");
        }
    }
}
