using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_CpmRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CpmRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DataDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProjectDurationDays = table.Column<int>(type: "int", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Trigger = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CpmRuns", x => x.Id);
                    table.CheckConstraint("CK_CpmRuns_ProjectDurationDays", "[ProjectDurationDays] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CpmRunActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpmRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    EarlyStart = table.Column<int>(type: "int", nullable: false),
                    EarlyFinish = table.Column<int>(type: "int", nullable: false),
                    LateStart = table.Column<int>(type: "int", nullable: false),
                    LateFinish = table.Column<int>(type: "int", nullable: false),
                    TotalFloat = table.Column<int>(type: "int", nullable: false),
                    FreeFloat = table.Column<int>(type: "int", nullable: false),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CpmRunActivities", x => x.Id);
                    table.CheckConstraint("CK_CpmRunActivities_DurationDays", "[DurationDays] >= 0");
                    table.ForeignKey(
                        name: "FK_CpmRunActivities_CpmRuns_CpmRunId",
                        column: x => x.CpmRunId,
                        principalTable: "CpmRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CpmRunRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpmRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PredecessorActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuccessorActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationType = table.Column<int>(type: "int", nullable: false),
                    LagDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CpmRunRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CpmRunRelations_CpmRuns_CpmRunId",
                        column: x => x.CpmRunId,
                        principalTable: "CpmRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CpmRunActivities_CpmRunId",
                table: "CpmRunActivities",
                column: "CpmRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CpmRunActivities_TenantId_CpmRunId",
                table: "CpmRunActivities",
                columns: new[] { "TenantId", "CpmRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_CpmRunRelations_CpmRunId",
                table: "CpmRunRelations",
                column: "CpmRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CpmRunRelations_TenantId_CpmRunId",
                table: "CpmRunRelations",
                columns: new[] { "TenantId", "CpmRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_CpmRuns_TenantId_ProjectId_CalculatedAt",
                table: "CpmRuns",
                columns: new[] { "TenantId", "ProjectId", "CalculatedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CpmRunActivities");

            migrationBuilder.DropTable(
                name: "CpmRunRelations");

            migrationBuilder.DropTable(
                name: "CpmRuns");
        }
    }
}
