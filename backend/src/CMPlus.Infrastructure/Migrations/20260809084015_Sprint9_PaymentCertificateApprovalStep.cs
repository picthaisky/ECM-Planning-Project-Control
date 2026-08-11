using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint9_PaymentCertificateApprovalStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSelfApproval",
                table: "PaymentCertificates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaymentCertificateApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    RequiredRole = table.Column<int>(type: "int", nullable: false),
                    QuorumCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCertificateApprovalSteps", x => x.Id);
                    table.CheckConstraint("CK_PaymentCertificateApprovalSteps_QuorumCount", "[QuorumCount] >= 1");
                    table.CheckConstraint("CK_PaymentCertificateApprovalSteps_RevisionNo", "[RevisionNo] >= 1");
                    table.CheckConstraint("CK_PaymentCertificateApprovalSteps_StepNo", "[StepNo] >= 1");
                    table.ForeignKey(
                        name: "FK_PaymentCertificateApprovalSteps_PaymentCertificates_PaymentCertificateId",
                        column: x => x.PaymentCertificateId,
                        principalTable: "PaymentCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificateApprovalSteps_PaymentCertificateId",
                table: "PaymentCertificateApprovalSteps",
                column: "PaymentCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificateApprovalSteps_TenantId_PaymentCertificateId_RevisionNo_StepNo",
                table: "PaymentCertificateApprovalSteps",
                columns: new[] { "TenantId", "PaymentCertificateId", "RevisionNo", "StepNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentCertificateApprovalSteps");

            migrationBuilder.DropColumn(
                name: "AllowSelfApproval",
                table: "PaymentCertificates");
        }
    }
}
