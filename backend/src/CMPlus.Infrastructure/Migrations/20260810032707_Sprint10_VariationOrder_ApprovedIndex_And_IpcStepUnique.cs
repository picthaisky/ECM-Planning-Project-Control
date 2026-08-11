using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint10_VariationOrder_ApprovedIndex_And_IpcStepUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentCertificateApprovalSteps_TenantId_PaymentCertificateId_RevisionNo_StepNo",
                table: "PaymentCertificateApprovalSteps");

            migrationBuilder.CreateIndex(
                name: "IX_VariationOrders_TenantId_ProjectId_Approved",
                table: "VariationOrders",
                columns: new[] { "TenantId", "ProjectId" },
                filter: "[Status] = 3")
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificateApprovalSteps_TenantId_PaymentCertificateId_RevisionNo_StepNo",
                table: "PaymentCertificateApprovalSteps",
                columns: new[] { "TenantId", "PaymentCertificateId", "RevisionNo", "StepNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VariationOrders_TenantId_ProjectId_Approved",
                table: "VariationOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCertificateApprovalSteps_TenantId_PaymentCertificateId_RevisionNo_StepNo",
                table: "PaymentCertificateApprovalSteps");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCertificateApprovalSteps_TenantId_PaymentCertificateId_RevisionNo_StepNo",
                table: "PaymentCertificateApprovalSteps",
                columns: new[] { "TenantId", "PaymentCertificateId", "RevisionNo", "StepNo" });
        }
    }
}
