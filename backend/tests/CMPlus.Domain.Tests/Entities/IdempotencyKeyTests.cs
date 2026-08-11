using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S13-BE-01/S13-DB-01: construction, validation, and the InProgress -&gt; Completed
/// one-way transition <see cref="IdempotencyKey.Complete"/> enforces.</summary>
public class IdempotencyKeyTests
{
    private static readonly DateTimeOffset ReservedAt = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    private static string Hash(string seed = "a") => new(seed[0], 64);

    private static IdempotencyKey CreateKey(
        Guid? tenantId = null,
        string key = "11111111-1111-1111-1111-111111111111",
        string requestMethod = "POST",
        string requestPath = "/api/v1/projects/00000000-0000-0000-0000-000000000001/photos",
        string? requestHash = null,
        Guid? requestedByUserId = null,
        DateTimeOffset? reservedAt = null) =>
        new(
            tenantId ?? Guid.NewGuid(),
            key,
            requestMethod,
            requestPath,
            requestHash ?? Hash(),
            requestedByUserId ?? Guid.NewGuid(),
            reservedAt ?? ReservedAt);

    [Fact]
    public void Constructor_Assigns_All_Fields_And_Starts_InProgress()
    {
        var tenantId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var hash = Hash("b");

        var record = new IdempotencyKey(
            tenantId, "key-123", "POST", "/api/v1/projects/x/weather-logs", hash, requestedByUserId, ReservedAt);

        Assert.Equal(tenantId, record.TenantId);
        Assert.Equal("key-123", record.Key);
        Assert.Equal("POST", record.RequestMethod);
        Assert.Equal("/api/v1/projects/x/weather-logs", record.RequestPath);
        Assert.Equal(hash, record.RequestHash);
        Assert.Equal(requestedByUserId, record.RequestedByUserId);
        Assert.Equal(ReservedAt, record.ReservedAt);
        Assert.Equal(IdempotencyRequestStatus.InProgress, record.Status);
        Assert.Null(record.CompletedAt);
        Assert.Null(record.ResponseStatusCode);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_TenantId()
    {
        Assert.Throws<DomainException>(() => CreateKey(tenantId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_A_Blank_Key(string blankKey)
    {
        Assert.Throws<DomainException>(() => CreateKey(key: blankKey));
    }

    [Fact]
    public void Constructor_Rejects_A_Key_Over_MaxKeyLength_Characters()
    {
        Assert.Throws<DomainException>(() => CreateKey(key: new string('k', IdempotencyKey.MaxKeyLength + 1)));
    }

    [Fact]
    public void Constructor_Accepts_A_Key_At_Exactly_MaxKeyLength_Characters()
    {
        var record = CreateKey(key: new string('k', IdempotencyKey.MaxKeyLength));
        Assert.Equal(IdempotencyKey.MaxKeyLength, record.Key.Length);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_RequestedByUserId_Fail_Closed_On_A_Null_Actor()
    {
        Assert.Throws<DomainException>(() => CreateKey(requestedByUserId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void Constructor_Rejects_A_RequestHash_That_Is_Not_A_64_Character_Hex_Digest(string badHash)
    {
        Assert.Throws<DomainException>(() => CreateKey(requestHash: badHash));
    }

    // ------------------------------------------------------------------------------------
    // Complete: the sole terminal transition.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Complete_Transitions_To_Completed_And_Stores_The_Response_Snapshot()
    {
        var record = CreateKey();
        var completedAt = ReservedAt.AddSeconds(2);

        record.Complete(201, "application/json", "{\"id\":\"abc\"}", responseNotReplayable: false, completedAt);

        Assert.Equal(IdempotencyRequestStatus.Completed, record.Status);
        Assert.Equal(201, record.ResponseStatusCode);
        Assert.Equal("application/json", record.ResponseContentType);
        Assert.Equal("{\"id\":\"abc\"}", record.ResponseBody);
        Assert.False(record.ResponseNotReplayable);
        Assert.Equal(completedAt, record.CompletedAt);
    }

    [Fact]
    public void Complete_Cannot_Run_Twice()
    {
        var record = CreateKey();
        record.Complete(201, "application/json", "{}", responseNotReplayable: false, ReservedAt.AddSeconds(1));

        Assert.Throws<DomainException>(() =>
            record.Complete(201, "application/json", "{}", responseNotReplayable: false, ReservedAt.AddSeconds(2)));
    }

    [Fact]
    public void Complete_Rejects_A_Non_Null_Body_When_Marked_Not_Replayable()
    {
        var record = CreateKey();

        Assert.Throws<DomainException>(() =>
            record.Complete(201, "application/json", "{}", responseNotReplayable: true, ReservedAt.AddSeconds(1)));
    }

    [Fact]
    public void Complete_Accepts_A_Null_Body_When_Marked_Not_Replayable()
    {
        var record = CreateKey();

        record.Complete(201, "application/json", null, responseNotReplayable: true, ReservedAt.AddSeconds(1));

        Assert.True(record.ResponseNotReplayable);
        Assert.Null(record.ResponseBody);
    }
}
