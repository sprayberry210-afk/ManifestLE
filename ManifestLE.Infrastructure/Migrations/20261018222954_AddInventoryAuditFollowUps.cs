using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAuditFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryAuditFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryAuditItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuditFollowUps", x => x.Id);
                    table.CheckConstraint("CK_InventoryAuditFollowUps_Notes", "length(trim(\"Notes\")) > 0\r\nAND length(\"Notes\") <= 4000");
                    table.CheckConstraint("CK_InventoryAuditFollowUps_Type", "\"Type\" IN ('InvestigationNote', 'Resolved')");
                    table.ForeignKey(
                        name: "FK_InventoryAuditFollowUps_InventoryAuditItems_InventoryAuditItemId",
                        column: x => x.InventoryAuditItemId,
                        principalTable: "InventoryAuditItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditFollowUps_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditFollowUps_InventoryAuditItemId_RecordedAt",
                table: "InventoryAuditFollowUps",
                columns: new[] { "InventoryAuditItemId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditFollowUps_RecordedByUserId",
                table: "InventoryAuditFollowUps",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryAuditFollowUps_OneResolutionPerItem",
                table: "InventoryAuditFollowUps",
                column: "InventoryAuditItemId",
                unique: true,
                filter: "\"Type\" = 'Resolved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAuditFollowUps");
        }
    }
}
