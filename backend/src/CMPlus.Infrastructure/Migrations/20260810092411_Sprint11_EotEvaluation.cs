using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_EotEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EotEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EvaluatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriticalityBasis = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<int>(type: "int", nullable: false),
                    AsScheduledDurationDays = table.Column<int>(type: "int", nullable: false),
                    ImpactedDurationDays = table.Column<int>(type: "int", nullable: false),
                    EotEligibleDays = table.Column<int>(type: "int", nullable: false),
                    CountableStoppageDayCount = table.Column<int>(type: "int", nullable: false),
                    DistinctCountableDateCount = table.Column<int>(type: "int", nullable: false),
                    UnattributedStoppageDayCount = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyAssessed = table.Column<bool>(type: "bit", nullable: false),
                    EntitlementBasisAssessed = table.Column<bool>(type: "bit", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LatestNoticeDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NoticeWindowExpired = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EotEvaluations", x => x.Id);
                    table.CheckConstraint("CK_EotEvaluations_Counts", "[CountableStoppageDayCount] >= 0 AND [DistinctCountableDateCount] >= 0 AND [UnattributedStoppageDayCount] >= 0");
                    table.CheckConstraint("CK_EotEvaluations_Durations", "[AsScheduledDurationDays] >= 0 AND [ImpactedDurationDays] >= 0 AND [EotEligibleDays] >= 0");
                    table.CheckConstraint("CK_EotEvaluations_Window", "[WindowEnd] >= [WindowStart]");
                });

            migrationBuilder.CreateTable(
                name: "ProjectEotPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartialDayPolicy = table.Column<int>(type: "int", nullable: false),
                    FullDayHours = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    MinHoursLostForCountableDay = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    MinRainfallMmForCountableDay = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    CountUnmeasuredRainfallWhenThresholdSet = table.Column<bool>(type: "bit", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEotPolicies", x => x.Id);
                    table.CheckConstraint("CK_ProjectEotPolicies_FullDayHours", "[FullDayHours] > 0 AND [FullDayHours] <= 24");
                    table.CheckConstraint("CK_ProjectEotPolicies_MinHoursLostForCountableDay", "[MinHoursLostForCountableDay] >= 0 AND [MinHoursLostForCountableDay] <= 24");
                    table.CheckConstraint("CK_ProjectEotPolicies_MinRainfallMmForCountableDay", "[MinRainfallMmForCountableDay] IS NULL OR [MinRainfallMmForCountableDay] >= 0");
                    table.CheckConstraint("CK_ProjectEotPolicies_NoticePeriodDays", "[NoticePeriodDays] IS NULL OR [NoticePeriodDays] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "EotEvaluationDrivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EotEvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpmRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActivityName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    StoppageDays = table.Column<int>(type: "int", nullable: false),
                    TotalFloatAtRun = table.Column<int>(type: "int", nullable: false),
                    WasCriticalAtRun = table.Column<bool>(type: "bit", nullable: false),
                    IsOnImpactedCriticalPath = table.Column<bool>(type: "bit", nullable: false),
                    IndicativeEotDays = table.Column<int>(type: "int", nullable: false),
                    MarginalEotDays = table.Column<int>(type: "int", nullable: false),
                    RemainingFloatAfter = table.Column<int>(type: "int", nullable: false),
                    UnclaimedFractionalHours = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EotEvaluationDrivers", x => x.Id);
                    table.CheckConstraint("CK_EotEvaluationDrivers_Counts", "[StoppageDays] >= 0 AND [IndicativeEotDays] >= 0 AND [MarginalEotDays] >= 0 AND [RemainingFloatAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_EotEvaluationDrivers_EotEvaluations_EotEvaluationId",
                        column: x => x.EotEvaluationId,
                        principalTable: "EotEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EotEvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EotEvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CpmRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WindowTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AsScheduledDurationDays = table.Column<int>(type: "int", nullable: false),
                    ImpactedDurationDays = table.Column<int>(type: "int", nullable: false),
                    DeltaDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EotEvaluationRuns", x => x.Id);
                    table.CheckConstraint("CK_EotEvaluationRuns_Window", "[WindowTo] >= [WindowFrom]");
                    table.ForeignKey(
                        name: "FK_EotEvaluationRuns_EotEvaluations_EotEvaluationId",
                        column: x => x.EotEvaluationId,
                        principalTable: "EotEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EotEvaluationSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EotEvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyWeatherLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountableDays = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ExclusionReason = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EotEvaluationSources", x => x.Id);
                    table.CheckConstraint("CK_EotEvaluationSources_CountableDays", "[CountableDays] >= 0");
                    table.ForeignKey(
                        name: "FK_EotEvaluationSources_EotEvaluations_EotEvaluationId",
                        column: x => x.EotEvaluationId,
                        principalTable: "EotEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationDrivers_EotEvaluationId",
                table: "EotEvaluationDrivers",
                column: "EotEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationDrivers_TenantId_EotEvaluationId",
                table: "EotEvaluationDrivers",
                columns: new[] { "TenantId", "EotEvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationRuns_EotEvaluationId",
                table: "EotEvaluationRuns",
                column: "EotEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationRuns_TenantId_CpmRunId",
                table: "EotEvaluationRuns",
                columns: new[] { "TenantId", "CpmRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationRuns_TenantId_EotEvaluationId",
                table: "EotEvaluationRuns",
                columns: new[] { "TenantId", "EotEvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluations_TenantId_ProjectId_EvaluatedAt",
                table: "EotEvaluations",
                columns: new[] { "TenantId", "ProjectId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationSources_EotEvaluationId",
                table: "EotEvaluationSources",
                column: "EotEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationSources_TenantId_DailyWeatherLogId",
                table: "EotEvaluationSources",
                columns: new[] { "TenantId", "DailyWeatherLogId" });

            migrationBuilder.CreateIndex(
                name: "IX_EotEvaluationSources_TenantId_EotEvaluationId",
                table: "EotEvaluationSources",
                columns: new[] { "TenantId", "EotEvaluationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEotPolicies_TenantId_ProjectId",
                table: "ProjectEotPolicies",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EotEvaluationDrivers");

            migrationBuilder.DropTable(
                name: "EotEvaluationRuns");

            migrationBuilder.DropTable(
                name: "EotEvaluationSources");

            migrationBuilder.DropTable(
                name: "ProjectEotPolicies");

            migrationBuilder.DropTable(
                name: "EotEvaluations");
        }
    }
}
