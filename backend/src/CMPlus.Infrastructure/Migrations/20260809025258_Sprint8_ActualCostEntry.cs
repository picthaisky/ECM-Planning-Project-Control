using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint8_ActualCostEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EvmPeriodSnapshots_Ac",
                table: "EvmPeriodSnapshots");

            migrationBuilder.CreateTable(
                name: "ActualCostEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WbsNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCategory = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IncurredDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CostCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VendorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileImportJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaidDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualCostEntries", x => x.Id);
                    table.CheckConstraint("CK_ActualCostEntries_Amount_NotZero", "[Amount] <> 0");
                    table.CheckConstraint("CK_ActualCostEntries_Quantity", "[Quantity] IS NULL OR [Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_ActualCostEntries_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualCostEntries_ActualCostEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "ActualCostEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualCostEntries_FileImportJobs_FileImportJobId",
                        column: x => x.FileImportJobId,
                        principalTable: "FileImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualCostEntries_WBSNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "WBSNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_ActivityId",
                table: "ActualCostEntries",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_FileImportJobId",
                table: "ActualCostEntries",
                column: "FileImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_ReversesEntryId",
                table: "ActualCostEntries",
                column: "ReversesEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_TenantId_ActivityId_IncurredDate",
                table: "ActualCostEntries",
                columns: new[] { "TenantId", "ActivityId", "IncurredDate" },
                filter: "[ActivityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_TenantId_ProjectId_CostCategory_IncurredDate",
                table: "ActualCostEntries",
                columns: new[] { "TenantId", "ProjectId", "CostCategory", "IncurredDate" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_TenantId_ProjectId_IncurredDate",
                table: "ActualCostEntries",
                columns: new[] { "TenantId", "ProjectId", "IncurredDate" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_TenantId_WbsNodeId_IncurredDate",
                table: "ActualCostEntries",
                columns: new[] { "TenantId", "WbsNodeId", "IncurredDate" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualCostEntries_WbsNodeId",
                table: "ActualCostEntries",
                column: "WbsNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualCostEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EvmPeriodSnapshots_Ac",
                table: "EvmPeriodSnapshots",
                sql: "[Ac] >= 0");
        }
    }
}
