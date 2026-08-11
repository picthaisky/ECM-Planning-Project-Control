using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_Project_ApprovedVariationOrderCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedVariationOrderCount",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_ApprovedVariationOrderCount",
                table: "Projects",
                sql: "[ApprovedVariationOrderCount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_ApprovedVariationOrderCount",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ApprovedVariationOrderCount",
                table: "Projects");
        }
    }
}
