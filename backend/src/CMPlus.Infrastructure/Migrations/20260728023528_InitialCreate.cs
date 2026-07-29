using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WbsNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PlannedStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PlannedFinish = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActualStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualFinish = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    BudgetCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProgressPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LatestProgressPeriodEndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LatestProgressRecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    TotalFloat = table.Column<int>(type: "int", nullable: true),
                    FreeFloat = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.CheckConstraint("CK_Activities_BudgetCost", "[BudgetCost] >= 0");
                    table.CheckConstraint("CK_Activities_DurationDays", "[DurationDays] >= 0");
                    table.CheckConstraint("CK_Activities_ProgressPercentage", "[ProgressPercentage] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "ActivityRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessorActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationType = table.Column<int>(type: "int", nullable: false),
                    LagDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRelations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    WorkingDays = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ContractStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContractFinish = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BAC = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RetentionRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    AdvanceRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    DataDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EacVariantDefault = table.Column<int>(type: "int", nullable: false),
                    EacCustomPerformanceFactor = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    EacManualEtc = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ContractValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RetentionCapPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RetentionRelease1Percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DefectsLiabilityMonths = table.Column<int>(type: "int", nullable: true),
                    AdvanceAmountPaid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AdvanceRecoveryMethod = table.Column<int>(type: "int", nullable: false),
                    AdvanceRecoveryStartPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    AdvanceRecoveryRatePct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    AdvanceRecoveryEndPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.CheckConstraint("CK_Projects_AdvanceAmountPaid", "[AdvanceAmountPaid] >= 0");
                    table.CheckConstraint("CK_Projects_AdvanceRate", "[AdvanceRate] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_AdvanceRecoveryEndPct", "[AdvanceRecoveryEndPct] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_AdvanceRecoveryRatePct", "[AdvanceRecoveryRatePct] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_AdvanceRecoveryStartPct", "[AdvanceRecoveryStartPct] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_BAC", "[BAC] >= 0");
                    table.CheckConstraint("CK_Projects_ContractValue", "[ContractValue] >= 0");
                    table.CheckConstraint("CK_Projects_EacCustomPerformanceFactor", "[EacCustomPerformanceFactor] > 0");
                    table.CheckConstraint("CK_Projects_EacManualEtc", "[EacManualEtc] >= 0");
                    table.CheckConstraint("CK_Projects_RetentionCapPercentage", "[RetentionCapPercentage] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_RetentionRate", "[RetentionRate] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Projects_RetentionRelease1Percentage", "[RetentionRelease1Percentage] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WBSNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentWbsNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    WeightPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WBSNodes", x => x.Id);
                    table.CheckConstraint("CK_WBSNodes_WeightPercentage", "[WeightPercentage] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_WBSNodes_WBSNodes_ParentWbsNodeId",
                        column: x => x.ParentWbsNodeId,
                        principalTable: "WBSNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityProgressLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodEndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProgressPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityProgressLogs", x => x.Id);
                    table.CheckConstraint("CK_ActivityProgressLogs_ActualQuantity", "[ActualQuantity] >= 0");
                    table.CheckConstraint("CK_ActivityProgressLogs_ProgressPercentage", "[ProgressPercentage] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ActivityProgressLogs_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_TenantId_WbsNodeId",
                table: "Activities",
                columns: new[] { "TenantId", "WbsNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityProgressLogs_ActivityId",
                table: "ActivityProgressLogs",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityProgressLogs_TenantId_ActivityId_PeriodEndDate",
                table: "ActivityProgressLogs",
                columns: new[] { "TenantId", "ActivityId", "PeriodEndDate" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityProgressLogs_TenantId_PeriodEndDate",
                table: "ActivityProgressLogs",
                columns: new[] { "TenantId", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRelations_TenantId_PredecessorActivityId",
                table: "ActivityRelations",
                columns: new[] { "TenantId", "PredecessorActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRelations_TenantId_SuccessorActivityId",
                table: "ActivityRelations",
                columns: new[] { "TenantId", "SuccessorActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_CalendarId",
                table: "CalendarExceptions",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_TenantId_CalendarId",
                table: "CalendarExceptions",
                columns: new[] { "TenantId", "CalendarId" });

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_TenantId_ProjectId",
                table: "Calendars",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Code",
                table: "Projects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WBSNodes_ParentWbsNodeId",
                table: "WBSNodes",
                column: "ParentWbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId",
                table: "WBSNodes",
                columns: new[] { "TenantId", "ProjectId", "ParentWbsNodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityProgressLogs");

            migrationBuilder.DropTable(
                name: "ActivityRelations");

            migrationBuilder.DropTable(
                name: "CalendarExceptions");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WBSNodes");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Calendars");
        }
    }
}
