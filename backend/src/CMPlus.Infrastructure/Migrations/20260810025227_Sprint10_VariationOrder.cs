using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint10_VariationOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EacManualEtcStaleSince",
                table: "Projects",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VariationOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TimeImpactDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    CurrentStepNo = table.Column<int>(type: "int", nullable: false),
                    TotalSteps = table.Column<int>(type: "int", nullable: false),
                    ApprovalPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalPolicyVersion = table.Column<int>(type: "int", nullable: true),
                    AllowSelfApproval = table.Column<bool>(type: "bit", nullable: false),
                    LastVoteAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BacBefore = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BacAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ContractValueBefore = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ContractValueAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CumulativeVoPctAtApproval = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    EscalationBasisContractValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariationOrders", x => x.Id);
                    table.CheckConstraint("CK_VariationOrders_CurrentStepNo", "[CurrentStepNo] >= 0");
                    table.CheckConstraint("CK_VariationOrders_RevisionNo", "[RevisionNo] >= 1");
                    table.CheckConstraint("CK_VariationOrders_TotalSteps", "[TotalSteps] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "VariationOrderApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariationOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    RequiredRole = table.Column<int>(type: "int", nullable: false),
                    QuorumCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariationOrderApprovalSteps", x => x.Id);
                    table.CheckConstraint("CK_VariationOrderApprovalSteps_QuorumCount", "[QuorumCount] >= 1");
                    table.CheckConstraint("CK_VariationOrderApprovalSteps_RevisionNo", "[RevisionNo] >= 1");
                    table.CheckConstraint("CK_VariationOrderApprovalSteps_StepNo", "[StepNo] >= 1");
                    table.ForeignKey(
                        name: "FK_VariationOrderApprovalSteps_VariationOrders_VariationOrderId",
                        column: x => x.VariationOrderId,
                        principalTable: "VariationOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VariationOrderScopeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariationOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetCostDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariationOrderScopeItems", x => x.Id);
                    table.CheckConstraint("CK_VariationOrderScopeItems_BudgetCostDelta_NotZero", "[BudgetCostDelta] <> 0");
                    table.ForeignKey(
                        name: "FK_VariationOrderScopeItems_VariationOrders_VariationOrderId",
                        column: x => x.VariationOrderId,
                        principalTable: "VariationOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrderApprovalSteps_TenantId_VariationOrderId_RevisionNo_StepNo",
                table: "VariationOrderApprovalSteps",
                columns: new[] { "TenantId", "VariationOrderId", "RevisionNo", "StepNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrderApprovalSteps_VariationOrderId",
                table: "VariationOrderApprovalSteps",
                column: "VariationOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrders_TenantId_ProjectId_Status",
                table: "VariationOrders",
                columns: new[] { "TenantId", "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrders_TenantId_ProjectId_VoNumber",
                table: "VariationOrders",
                columns: new[] { "TenantId", "ProjectId", "VoNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrderScopeItems_TenantId_ActivityId",
                table: "VariationOrderScopeItems",
                columns: new[] { "TenantId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrderScopeItems_TenantId_VariationOrderId",
                table: "VariationOrderScopeItems",
                columns: new[] { "TenantId", "VariationOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrderScopeItems_VariationOrderId",
                table: "VariationOrderScopeItems",
                column: "VariationOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VariationOrderApprovalSteps");

            migrationBuilder.DropTable(
                name: "VariationOrderScopeItems");

            migrationBuilder.DropTable(
                name: "VariationOrders");

            migrationBuilder.DropColumn(
                name: "EacManualEtcStaleSince",
                table: "Projects");
        }
    }
}
