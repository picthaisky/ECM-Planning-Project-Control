using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint12_ManpowerEquipmentLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BudgetManHours",
                table: "Activities",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManpowerEquipmentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Shift = table.Column<int>(type: "int", nullable: false),
                    WorkCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WbsNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LabourType = table.Column<int>(type: "int", nullable: false),
                    SubcontractorRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkerCount = table.Column<int>(type: "int", nullable: false),
                    ManHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ManHoursDerived = table.Column<bool>(type: "bit", nullable: false),
                    EquipmentCount = table.Column<int>(type: "int", nullable: false),
                    EquipmentOperatingHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    EquipmentStandbyHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    WorkDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedWeatherLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EntryKind = table.Column<int>(type: "int", nullable: false),
                    CorrectsLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AllowDuplicateOverride = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManpowerEquipmentLogs", x => x.Id);
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_EquipmentCount", "[EquipmentCount] >= 0");
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_EquipmentOperatingHours", "[EquipmentOperatingHours] >= 0");
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_EquipmentStandbyHours", "[EquipmentStandbyHours] >= 0");
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_ManHours", "[ManHours] >= 0");
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_OvertimeHours", "[OvertimeHours] >= 0 AND [OvertimeHours] <= [ManHours]");
                    table.CheckConstraint("CK_ManpowerEquipmentLogs_WorkerCount", "[WorkerCount] >= 0");
                    table.ForeignKey(
                        name: "FK_ManpowerEquipmentLogs_ManpowerEquipmentLogs_CorrectsLogId",
                        column: x => x.CorrectsLogId,
                        principalTable: "ManpowerEquipmentLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManpowerPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WbsNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PlannedWorkerCount = table.Column<int>(type: "int", nullable: true),
                    PlannedManHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManpowerPlans", x => x.Id);
                    table.CheckConstraint("CK_ManpowerPlans_EffectiveRange", "[EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_ManpowerPlans_PlannedManHours", "[PlannedManHours] IS NULL OR [PlannedManHours] >= 0");
                    table.CheckConstraint("CK_ManpowerPlans_PlannedWorkerCount", "[PlannedWorkerCount] IS NULL OR [PlannedWorkerCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "WorkCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    NameTh = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerEquipmentLogs_CorrectsLogId",
                table: "ManpowerEquipmentLogs",
                column: "CorrectsLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerEquipmentLogs_TenantId_CorrectsLogId",
                table: "ManpowerEquipmentLogs",
                columns: new[] { "TenantId", "CorrectsLogId" },
                unique: true,
                filter: "[CorrectsLogId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerEquipmentLogs_TenantId_ProjectId_ActivityId_LogDate",
                table: "ManpowerEquipmentLogs",
                columns: new[] { "TenantId", "ProjectId", "ActivityId", "LogDate" },
                filter: "[ActivityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerEquipmentLogs_TenantId_ProjectId_LogDate",
                table: "ManpowerEquipmentLogs",
                columns: new[] { "TenantId", "ProjectId", "LogDate" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerEquipmentLogs_TenantId_ProjectId_WbsNodeId_LogDate",
                table: "ManpowerEquipmentLogs",
                columns: new[] { "TenantId", "ProjectId", "WbsNodeId", "LogDate" },
                filter: "[WbsNodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPlans_TenantId_ProjectId_WbsNodeId_EffectiveFrom_EffectiveTo",
                table: "ManpowerPlans",
                columns: new[] { "TenantId", "ProjectId", "WbsNodeId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCategories_TenantId_Code",
                table: "WorkCategories",
                columns: new[] { "TenantId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCategories_TenantId_ProjectId",
                table: "WorkCategories",
                columns: new[] { "TenantId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManpowerEquipmentLogs");

            migrationBuilder.DropTable(
                name: "ManpowerPlans");

            migrationBuilder.DropTable(
                name: "WorkCategories");

            migrationBuilder.DropColumn(
                name: "BudgetManHours",
                table: "Activities");
        }
    }
}
