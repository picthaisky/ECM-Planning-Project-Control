using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Sprint-10 security review H-02 (amended before first application - see
    /// <c>Sprint10_Project_OriginalContractValue</c>'s identical remark). The original version of this
    /// migration added the column nullable, with no backfill, so <c>GetBacAsOfAsync</c>'s <c>??</c>
    /// fallback to the live <c>BAC</c> double-counted every already-approved VO for a pre-existing
    /// row. Fixed the same way: backfill, then tighten to NOT NULL in the same migration.
    /// </remarks>
    public partial class Sprint10_Project_OriginalBac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OriginalBac",
                table: "Projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // H-02 backfill: every row that exists when this migration runs gets OriginalBac = its own
            // current BAC - correct because no VariationOrder can exist yet, so BAC cannot yet have
            // drifted from its original, as-baselined figure for any row.
            migrationBuilder.Sql(
                "UPDATE [Projects] SET [OriginalBac] = [BAC] WHERE [OriginalBac] IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "OriginalBac",
                table: "Projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_OriginalBac",
                table: "Projects",
                sql: "[OriginalBac] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_OriginalBac",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OriginalBac",
                table: "Projects");
        }
    }
}
