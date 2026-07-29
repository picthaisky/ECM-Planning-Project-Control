using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S3-BE-04: a <see cref="FileImportJob"/> starts <see cref="ImportJobStatus.Pending"/>
/// and transitions exactly once to a terminal state - never back, never twice.</summary>
public class FileImportJobTests
{
    private static FileImportJob CreateJob() => new(
        Guid.NewGuid(), Guid.NewGuid(), "schedule.xer", ImportFileFormat.Xer,
        Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_Starts_In_Pending_With_No_Rows_Or_Error()
    {
        var job = CreateJob();

        Assert.Equal(ImportJobStatus.Pending, job.Status);
        Assert.Equal(0, job.RowsImported);
        Assert.Null(job.ErrorJson);
        Assert.Null(job.FinishedAt);
    }

    [Fact]
    public void Constructor_Rejects_A_Blank_FileName()
    {
        Assert.Throws<DomainException>(() => new FileImportJob(
            Guid.NewGuid(), Guid.NewGuid(), "   ", ImportFileFormat.Xer, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkSucceeded_Sets_Status_RowsImported_And_FinishedAt()
    {
        var job = CreateJob();
        var finishedAt = DateTimeOffset.UtcNow.AddSeconds(5);

        job.MarkSucceeded(rowsImported: 42, finishedAt);

        Assert.Equal(ImportJobStatus.Succeeded, job.Status);
        Assert.Equal(42, job.RowsImported);
        Assert.Equal(finishedAt, job.FinishedAt);
        Assert.Null(job.ErrorJson);
    }

    [Fact]
    public void MarkFailed_Sets_Status_ErrorJson_And_FinishedAt()
    {
        var job = CreateJob();
        var finishedAt = DateTimeOffset.UtcNow.AddSeconds(5);

        job.MarkFailed("""{"code":"ImportMalformedFile","detail":"bad file"}""", finishedAt);

        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal("""{"code":"ImportMalformedFile","detail":"bad file"}""", job.ErrorJson);
        Assert.Equal(finishedAt, job.FinishedAt);
        Assert.Equal(0, job.RowsImported);
    }

    [Fact]
    public void MarkFailed_Rejects_A_Blank_ErrorJson()
    {
        var job = CreateJob();

        Assert.Throws<DomainException>(() => job.MarkFailed("   ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkSucceeded_Rejects_A_Negative_RowsImported()
    {
        var job = CreateJob();

        Assert.Throws<DomainException>(() => job.MarkSucceeded(-1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_Job_Cannot_Be_Transitioned_Twice_Once_It_Has_Reached_A_Terminal_State()
    {
        var succeededJob = CreateJob();
        succeededJob.MarkSucceeded(1, DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => succeededJob.MarkSucceeded(2, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => succeededJob.MarkFailed("x", DateTimeOffset.UtcNow));

        var failedJob = CreateJob();
        failedJob.MarkFailed("x", DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => failedJob.MarkFailed("y", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => failedJob.MarkSucceeded(1, DateTimeOffset.UtcNow));
    }
}
