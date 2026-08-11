using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_DailyWeatherLog_IssueLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyWeatherLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    ConditionNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RainfallMm = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    ImpactNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HoursLost = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EntryKind = table.Column<int>(type: "int", nullable: false),
                    CorrectsWeatherLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyWeatherLogs", x => x.Id);
                    table.CheckConstraint("CK_DailyWeatherLogs_HoursLost", "[HoursLost] IS NULL OR [HoursLost] BETWEEN 0 AND 24");
                    table.CheckConstraint("CK_DailyWeatherLogs_RainfallMm", "[RainfallMm] IS NULL OR [RainfallMm] >= 0");
                    table.ForeignKey(
                        name: "FK_DailyWeatherLogs_DailyWeatherLogs_CorrectsWeatherLogId",
                        column: x => x.CorrectsWeatherLogId,
                        principalTable: "DailyWeatherLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssueLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLogs", x => x.Id);
                    table.CheckConstraint("CK_IssueLogs_ClosedAt_Matches_Status", "([Status] = 3 AND [ClosedAt] IS NOT NULL) OR ([Status] <> 3 AND [ClosedAt] IS NULL)");
                    table.CheckConstraint("CK_IssueLogs_StartedAt_Before_ClosedAt", "[StartedAt] IS NULL OR [ClosedAt] IS NULL OR [StartedAt] <= [ClosedAt]");
                    table.CheckConstraint("CK_IssueLogs_StartedAt_Matches_Status", "([Status] IN (2, 3) AND [StartedAt] IS NOT NULL) OR ([Status] = 1 AND [StartedAt] IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "DailyWeatherLogActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyWeatherLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyWeatherLogActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyWeatherLogActivities_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyWeatherLogActivities_DailyWeatherLogs_DailyWeatherLogId",
                        column: x => x.DailyWeatherLogId,
                        principalTable: "DailyWeatherLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogActivities_ActivityId",
                table: "DailyWeatherLogActivities",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogActivities_DailyWeatherLogId",
                table: "DailyWeatherLogActivities",
                column: "DailyWeatherLogId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogActivities_TenantId_ActivityId",
                table: "DailyWeatherLogActivities",
                columns: new[] { "TenantId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogActivities_TenantId_DailyWeatherLogId_ActivityId",
                table: "DailyWeatherLogActivities",
                columns: new[] { "TenantId", "DailyWeatherLogId", "ActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogs_CorrectsWeatherLogId",
                table: "DailyWeatherLogs",
                column: "CorrectsWeatherLogId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogs_TenantId_CorrectsWeatherLogId",
                table: "DailyWeatherLogs",
                columns: new[] { "TenantId", "CorrectsWeatherLogId" },
                unique: true,
                filter: "[CorrectsWeatherLogId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogs_TenantId_ProjectId_LogDate",
                table: "DailyWeatherLogs",
                columns: new[] { "TenantId", "ProjectId", "LogDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueLogs_TenantId_ProjectId_CreatedAt",
                table: "IssueLogs",
                columns: new[] { "TenantId", "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueLogs_TenantId_ProjectId_Status",
                table: "IssueLogs",
                columns: new[] { "TenantId", "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyWeatherLogActivities");

            migrationBuilder.DropTable(
                name: "IssueLogs");

            migrationBuilder.DropTable(
                name: "DailyWeatherLogs");
        }
    }
}
