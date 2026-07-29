using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_Auth_Audit_ApprovalPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRoleAtTime = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovalPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalPolicyVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AllowSelfApproval = table.Column<bool>(type: "bit", nullable: false),
                    CumulativeVoEscalationPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CumulativeVoEscalationRole = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicies", x => x.Id);
                    table.CheckConstraint("CK_ApprovalPolicies_CumulativeVoEscalationPct", "[CumulativeVoEscalationPct] IS NULL OR [CumulativeVoEscalationPct] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalPolicyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RequiredRole = table.Column<int>(type: "int", nullable: false),
                    RequiredUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuorumCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicyRules", x => x.Id);
                    table.CheckConstraint("CK_ApprovalPolicyRules_MaxAmount", "[MaxAmount] IS NULL OR [MaxAmount] > [MinAmount]");
                    table.CheckConstraint("CK_ApprovalPolicyRules_MinAmount", "[MinAmount] >= 0");
                    table.CheckConstraint("CK_ApprovalPolicyRules_QuorumCount", "[QuorumCount] >= 1");
                    table.CheckConstraint("CK_ApprovalPolicyRules_StepNo", "[StepNo] >= 1");
                    table.ForeignKey(
                        name: "FK_ApprovalPolicyRules_ApprovalPolicies_ApprovalPolicyId",
                        column: x => x.ApprovalPolicyId,
                        principalTable: "ApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_TenantId_DocumentType_DocumentId_RevisionNo_StepNo",
                table: "ApprovalActions",
                columns: new[] { "TenantId", "DocumentType", "DocumentId", "RevisionNo", "StepNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_TenantId_ProjectId_DocumentType",
                table: "ApprovalPolicies",
                columns: new[] { "TenantId", "ProjectId", "DocumentType" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicyRules_ApprovalPolicyId",
                table: "ApprovalPolicyRules",
                column: "ApprovalPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicyRules_TenantId_ApprovalPolicyId_StepNo",
                table: "ApprovalPolicyRules",
                columns: new[] { "TenantId", "ApprovalPolicyId", "StepNo" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalActions");

            migrationBuilder.DropTable(
                name: "ApprovalPolicyRules");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ApprovalPolicies");
        }
    }
}
