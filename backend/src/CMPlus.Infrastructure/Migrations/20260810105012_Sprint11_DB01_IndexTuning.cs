using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint11_DB01_IndexTuning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyWeatherLogs_TenantId_ProjectId_LogDate",
                table: "DailyWeatherLogs");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogs_TenantId_ProjectId_LogDate_RecordedAt",
                table: "DailyWeatherLogs",
                columns: new[] { "TenantId", "ProjectId", "LogDate", "RecordedAt" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyWeatherLogs_TenantId_ProjectId_LogDate_RecordedAt",
                table: "DailyWeatherLogs");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWeatherLogs_TenantId_ProjectId_LogDate",
                table: "DailyWeatherLogs",
                columns: new[] { "TenantId", "ProjectId", "LogDate" });
        }
    }
}
