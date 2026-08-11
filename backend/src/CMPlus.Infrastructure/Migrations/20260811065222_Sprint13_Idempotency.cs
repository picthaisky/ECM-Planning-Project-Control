using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPlus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint13_Idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequestPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseNotReplayable = table.Column<bool>(type: "bit", nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Id);
                    table.CheckConstraint("CK_IdempotencyKeys_ResponseBody_Length", "[ResponseBody] IS NULL OR LEN([ResponseBody]) <= 65536");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_TenantId_Key",
                table: "IdempotencyKeys",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_TenantId_Status_CompletedAt",
                table: "IdempotencyKeys",
                columns: new[] { "TenantId", "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_TenantId_Status_ReservedAt",
                table: "IdempotencyKeys",
                columns: new[] { "TenantId", "Status", "ReservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKeys");
        }
    }
}
