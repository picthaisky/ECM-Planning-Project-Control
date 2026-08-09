using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint9_PaymentCertificate_ProjectFinanceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MilestoneNo = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MilestoneValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousCumulativeApprovePct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ApprovePct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ClaimPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ActualProgressPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    GrossCertifiedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RetentionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AdvanceRecoveryAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetPayment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    CurrentStepNo = table.Column<int>(type: "int", nullable: false),
                    TotalSteps = table.Column<int>(type: "int", nullable: false),
                    ApprovalPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalPolicyVersion = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CertifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCertificates", x => x.Id);
                    table.CheckConstraint("CK_PaymentCertificates_ApprovePct", "[ApprovePct] >= 0 AND [ApprovePct] <= 100");
                    table.CheckConstraint("CK_PaymentCertificates_GrossCertifiedAmount", "[GrossCertifiedAmount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectFinanceLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFinanceLedgers", x => x.Id);
                    table.CheckConstraint("CK_ProjectFinanceLedgers_Amount_NotZero", "[Amount] <> 0");
                    table.ForeignKey(
                        name: "FK_ProjectFinanceLedgers_PaymentCertificates_PaymentCertificateId",
                        column: x => x.PaymentCertificateId,
                        principalTable: "PaymentCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificates_TenantId_ProjectId_MilestoneNo",
                table: "PaymentCertificates",
                columns: new[] { "TenantId", "ProjectId", "MilestoneNo" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificates_TenantId_ProjectId_Status",
                table: "PaymentCertificates",
                columns: new[] { "TenantId", "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFinanceLedgers_PaymentCertificateId",
                table: "ProjectFinanceLedgers",
                column: "PaymentCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFinanceLedgers_TenantId_ProjectId_Category",
                table: "ProjectFinanceLedgers",
                columns: new[] { "TenantId", "ProjectId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectFinanceLedgers");

            migrationBuilder.DropTable(
                name: "PaymentCertificates");
        }
    }
}
