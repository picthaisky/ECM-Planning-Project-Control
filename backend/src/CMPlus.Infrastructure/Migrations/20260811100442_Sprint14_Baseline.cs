using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint14_Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Baselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bac = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Baselines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BaselineActivitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PlannedFinish = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    BudgetCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaselineActivitySnapshots", x => x.Id);
                    table.CheckConstraint("CK_BaselineActivitySnapshots_DurationDays", "[DurationDays] >= 0");
                    table.ForeignKey(
                        name: "FK_BaselineActivitySnapshots_Baselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "Baselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BaselineActivitySnapshots_BaselineId",
                table: "BaselineActivitySnapshots",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_BaselineActivitySnapshots_TenantId_BaselineId_ActivityId",
                table: "BaselineActivitySnapshots",
                columns: new[] { "TenantId", "BaselineId", "ActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Baselines_TenantId_ProjectId_Active",
                table: "Baselines",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaselineActivitySnapshots");

            migrationBuilder.DropTable(
                name: "Baselines");
        }
    }
}
