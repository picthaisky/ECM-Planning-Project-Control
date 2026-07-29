using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4_WbsNodes_CoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId",
                table: "WBSNodes");

            migrationBuilder.CreateIndex(
                name: "IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId",
                table: "WBSNodes",
                columns: new[] { "TenantId", "ProjectId", "ParentWbsNodeId" })
                .Annotation("SqlServer:Include", new[] { "Code", "Title", "WeightPercentage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId",
                table: "WBSNodes");

            migrationBuilder.CreateIndex(
                name: "IX_WBSNodes_TenantId_ProjectId_ParentWbsNodeId",
                table: "WBSNodes",
                columns: new[] { "TenantId", "ProjectId", "ParentWbsNodeId" });
        }
    }
}
