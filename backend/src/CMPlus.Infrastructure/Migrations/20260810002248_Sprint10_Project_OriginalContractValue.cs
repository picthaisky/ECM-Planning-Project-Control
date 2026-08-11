using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Sprint-10 security review H-02 (amended before first application - neither this migration nor
    /// <c>Sprint10_Project_OriginalBac</c> had been applied to any real database): the original version
    /// of this migration added the column nullable, with no backfill. Every pre-existing project row
    /// then read <c>NULL</c>, and <c>Project.EscalationBaselineContractValue</c>'s <c>??</c> fallback
    /// silently degraded to the live, VO-inclusive <c>ContractValue</c> - reinstating the exact
    /// self-diluting-denominator defect ADR-0015 exists to remove. Fixed here: backfill every existing
    /// row (safe unconditionally, because no <see cref="CMPlus.Domain.Entities.VariationOrder"/> table
    /// exists yet at this point in the migration history, so no approved VO could ever have moved
    /// <c>ContractValue</c> away from its original figure) and tighten the column to NOT NULL in the
    /// same migration, rather than shipping a nullable column and hoping every future write path
    /// remembers to populate it.
    /// </remarks>
    public partial class Sprint10_Project_OriginalContractValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OriginalContractValue",
                table: "Projects",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // H-02 backfill: every row that exists when this migration runs gets OriginalContractValue
            // = its own current ContractValue - correct because no VariationOrder can exist yet (this
            // table is created by a later migration), so ContractValue cannot yet have drifted from
            // its original, as-signed figure for any row.
            migrationBuilder.Sql(
                "UPDATE [Projects] SET [OriginalContractValue] = [ContractValue] WHERE [OriginalContractValue] IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "OriginalContractValue",
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
                name: "CK_Projects_OriginalContractValue",
                table: "Projects",
                sql: "[OriginalContractValue] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_OriginalContractValue",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OriginalContractValue",
                table: "Projects");
        }
    }
}
