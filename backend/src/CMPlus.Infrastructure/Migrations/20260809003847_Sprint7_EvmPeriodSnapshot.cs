using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint7_EvmPeriodSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvmPeriodSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Bac = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Pv = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Ev = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Ac = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EacVariant = table.Column<int>(type: "int", nullable: false),
                    PerformanceFactor = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Eac = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Etc = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Vac = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvmPeriodSnapshots", x => x.Id);
                    table.CheckConstraint("CK_EvmPeriodSnapshots_Ac", "[Ac] >= 0");
                    table.CheckConstraint("CK_EvmPeriodSnapshots_Bac", "[Bac] >= 0");
                    table.CheckConstraint("CK_EvmPeriodSnapshots_Ev", "[Ev] >= 0");
                    table.CheckConstraint("CK_EvmPeriodSnapshots_Pv", "[Pv] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvmPeriodSnapshots_TenantId_ProjectId_DataDate",
                table: "EvmPeriodSnapshots",
                columns: new[] { "TenantId", "ProjectId", "DataDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvmPeriodSnapshots");
        }
    }
}
