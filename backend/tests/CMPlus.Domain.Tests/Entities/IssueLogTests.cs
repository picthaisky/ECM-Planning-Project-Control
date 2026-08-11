using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S11-BE-03 (US-11.2): construction, validation, and the strictly-linear
/// <c>Open -&gt; Doing -&gt; Closed</c> state machine.</summary>
public class IssueLogTests
{
    private static IssueLog CreateIssue(
        Guid? projectId = null,
        Guid? createdByUserId = null,
        string title = "น้ำรั่วซึมผนัง Basement โซน B",
        string? detail = "พบคราบน้ำหลังฝนตกหนัก 8 ก.ค.",
        string? owner = "วิศวกรโครงสร้าง",
        DateTimeOffset? dueDate = null,
        DateTimeOffset? createdAt = null) =>
        new(
            Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            title,
            detail,
            owner,
            dueDate,
            createdByUserId ?? Guid.NewGuid(),
            createdAt ?? DateTimeOffset.Parse("2026-07-08T09:00:00+07:00"));

    [Fact]
    public void Constructor_Assigns_All_Fields_And_Starts_Open_With_No_ClosedAt()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var dueDate = DateTimeOffset.Parse("2026-07-18T00:00:00+07:00");
        var createdAt = DateTimeOffset.Parse("2026-07-08T09:00:00+07:00");

        var issue = new IssueLog(
            tenantId, projectId, "เหล็กเส้น DB25 ส่งช้า", "ซัพพลายเออร์แจ้งเลื่อน 5 วัน", "จัดซื้อ",
            dueDate, createdByUserId, createdAt);

        Assert.Equal(tenantId, issue.TenantId);
        Assert.Equal(projectId, issue.ProjectId);
        Assert.Equal("เหล็กเส้น DB25 ส่งช้า", issue.Title);
        Assert.Equal("ซัพพลายเออร์แจ้งเลื่อน 5 วัน", issue.Detail);
        Assert.Equal("จัดซื้อ", issue.Owner);
        Assert.Equal(dueDate, issue.DueDate);
        Assert.Equal(createdByUserId, issue.CreatedByUserId);
        Assert.Equal(createdAt, issue.CreatedAt);

        // US-11.2 AC: "Given a new issue, when created, then Status = Open".
        Assert.Equal(IssueStatus.Open, issue.Status);
        Assert.Null(issue.ClosedAt);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => CreateIssue(projectId: Guid.Empty));
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_CreatedByUserId()
    {
        Assert.Throws<DomainException>(() => CreateIssue(createdByUserId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Rejects_A_Blank_Title(string? blank)
    {
        Assert.Throws<DomainException>(() => CreateIssue(title: blank!));
    }

    [Fact]
    public void Constructor_Trims_Title()
    {
        var issue = CreateIssue(title: "  แบบ Shop Drawing ลิฟต์ยังไม่อนุมัติ  ");
        Assert.Equal("แบบ Shop Drawing ลิฟต์ยังไม่อนุมัติ", issue.Title);
    }

    [Fact]
    public void Constructor_Allows_Detail_Owner_And_DueDate_To_Be_Null()
    {
        var issue = CreateIssue(detail: null, owner: null, dueDate: null);

        Assert.Null(issue.Detail);
        Assert.Null(issue.Owner);
        Assert.Null(issue.DueDate);
    }

    [Fact]
    public void AdvanceStatus_Moves_Open_To_Doing_Without_Stamping_ClosedAt()
    {
        var issue = CreateIssue();

        issue.AdvanceStatus(DateTimeOffset.Parse("2026-07-09T10:00:00+07:00"));

        Assert.Equal(IssueStatus.Doing, issue.Status);
        Assert.Null(issue.ClosedAt);
    }

    [Fact]
    public void AdvanceStatus_Moves_Doing_To_Closed_And_Stamps_ClosedAt_Only_Then()
    {
        var issue = CreateIssue();
        issue.AdvanceStatus(DateTimeOffset.Parse("2026-07-09T10:00:00+07:00")); // Open -> Doing

        var closedAt = DateTimeOffset.Parse("2026-07-10T16:30:00+07:00");
        issue.AdvanceStatus(closedAt); // Doing -> Closed

        Assert.Equal(IssueStatus.Closed, issue.Status);
        Assert.Equal(closedAt, issue.ClosedAt);
    }

    [Fact]
    public void AdvanceStatus_Never_Stamps_ClosedAt_On_The_Open_To_Doing_Step()
    {
        // S11-BE-03 DoD, read literally: ClosedAt is stamped ONLY on entry to Closed - not on any
        // earlier transition, however tempting "stamp every transition" might seem.
        var issue = CreateIssue();
        var openToDoingAt = DateTimeOffset.Parse("2026-07-09T10:00:00+07:00");

        issue.AdvanceStatus(openToDoingAt);

        Assert.Null(issue.ClosedAt);
    }

    [Fact]
    public void AdvanceStatus_Throws_Once_Already_Closed_No_Reopen()
    {
        var issue = CreateIssue();
        issue.AdvanceStatus(DateTimeOffset.UtcNow); // Open -> Doing
        issue.AdvanceStatus(DateTimeOffset.UtcNow); // Doing -> Closed

        var ex = Assert.Throws<DomainException>(() => issue.AdvanceStatus(DateTimeOffset.UtcNow));
        Assert.Contains("Closed", ex.Message);

        // Status/ClosedAt are unperturbed by the rejected attempt.
        Assert.Equal(IssueStatus.Closed, issue.Status);
    }

    [Fact]
    public void AdvanceStatus_Never_Skips_Doing_Straight_From_Open_To_Closed()
    {
        // Mirrors the prototype's single "next" button per row (relabels per state, never jumps) -
        // each call moves exactly one step.
        var issue = CreateIssue();

        issue.AdvanceStatus(DateTimeOffset.UtcNow);

        Assert.Equal(IssueStatus.Doing, issue.Status);
        Assert.NotEqual(IssueStatus.Closed, issue.Status);
    }
}
