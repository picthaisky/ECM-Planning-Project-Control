using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint15_ApprovalPolicy_SplitSingleActiveIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalPolicies_TenantId_ProjectId_DocumentType",
                table: "ApprovalPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_TenantId_DocumentType",
                table: "ApprovalPolicies",
                columns: new[] { "TenantId", "DocumentType" },
                unique: true,
                filter: "[IsActive] = 1 AND [ProjectId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_TenantId_ProjectId_DocumentType",
                table: "ApprovalPolicies",
                columns: new[] { "TenantId", "ProjectId", "DocumentType" },
                unique: true,
                filter: "[IsActive] = 1 AND [ProjectId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalPolicies_TenantId_DocumentType",
                table: "ApprovalPolicies");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalPolicies_TenantId_ProjectId_DocumentType",
                table: "ApprovalPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_TenantId_ProjectId_DocumentType",
                table: "ApprovalPolicies",
                columns: new[] { "TenantId", "ProjectId", "DocumentType" },
                unique: true,
                filter: "[IsActive] = 1");
        }
    }
}
