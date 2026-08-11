using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_EotEvaluation_SerialChainCollapse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EotEvaluations_Counts",
                table: "EotEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EotEvaluationDrivers_Counts",
                table: "EotEvaluationDrivers");

            migrationBuilder.AddColumn<bool>(
                name: "HoursLostClampedToFullDay",
                table: "EotEvaluationSources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SerialChainAbsorbedDayCount",
                table: "EotEvaluations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AbsorbedIntoActivityCodes",
                table: "EotEvaluationDrivers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SerialChainAbsorbedDays",
                table: "EotEvaluationDrivers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EotEvaluations_Counts",
                table: "EotEvaluations",
                sql: "[CountableStoppageDayCount] >= 0 AND [SerialChainAbsorbedDayCount] >= 0 AND [DistinctCountableDateCount] >= 0 AND [UnattributedStoppageDayCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EotEvaluationDrivers_Counts",
                table: "EotEvaluationDrivers",
                sql: "[StoppageDays] >= 0 AND [IndicativeEotDays] >= 0 AND [MarginalEotDays] >= 0 AND [RemainingFloatAfter] >= 0 AND [SerialChainAbsorbedDays] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EotEvaluations_Counts",
                table: "EotEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EotEvaluationDrivers_Counts",
                table: "EotEvaluationDrivers");

            migrationBuilder.DropColumn(
                name: "HoursLostClampedToFullDay",
                table: "EotEvaluationSources");

            migrationBuilder.DropColumn(
                name: "SerialChainAbsorbedDayCount",
                table: "EotEvaluations");

            migrationBuilder.DropColumn(
                name: "AbsorbedIntoActivityCodes",
                table: "EotEvaluationDrivers");

            migrationBuilder.DropColumn(
                name: "SerialChainAbsorbedDays",
                table: "EotEvaluationDrivers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EotEvaluations_Counts",
                table: "EotEvaluations",
                sql: "[CountableStoppageDayCount] >= 0 AND [DistinctCountableDateCount] >= 0 AND [UnattributedStoppageDayCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EotEvaluationDrivers_Counts",
                table: "EotEvaluationDrivers",
                sql: "[StoppageDays] >= 0 AND [IndicativeEotDays] >= 0 AND [MarginalEotDays] >= 0 AND [RemainingFloatAfter] >= 0");
        }
    }
}
