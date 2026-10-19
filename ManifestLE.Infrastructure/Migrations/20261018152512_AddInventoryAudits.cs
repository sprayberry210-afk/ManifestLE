using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoragePathSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeChildLocations = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAudits", x => x.Id);
                    table.CheckConstraint("CK_InventoryAudits_Completion", "(\r\n    \"Status\" = 'InProgress'\r\n    AND \"CompletedByUserId\" IS NULL\r\n    AND \"CompletedAt\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"Status\" = 'Completed'\r\n    AND \"CompletedByUserId\" IS NOT NULL\r\n    AND \"CompletedAt\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_InventoryAudits_Status", "\"Status\" IN ('InProgress', 'Completed')");
                    table.ForeignKey(
                        name: "FK_InventoryAudits_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAudits_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAudits_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAuditItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryAuditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FoundPropertyItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SafekeepingItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReportNumberSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    ItemNumberSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedStorageLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpectedStoragePathSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuditItems", x => x.Id);
                    table.CheckConstraint("CK_InventoryAuditItems_CheckDetails", "(\r\n    \"Result\" = 'Pending'\r\n    AND \"CheckedByUserId\" IS NULL\r\n    AND \"CheckedAt\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"Result\" IN ('Verified', 'Missing', 'Discrepancy')\r\n    AND \"CheckedByUserId\" IS NOT NULL\r\n    AND \"CheckedAt\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_InventoryAuditItems_ExactlyOneItem", "(\r\n    CASE WHEN \"EvidenceItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"FoundPropertyItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"SafekeepingItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n) = 1");
                    table.CheckConstraint("CK_InventoryAuditItems_ExpectedStatus", "\"ExpectedStatus\" = 'InCustody'");
                    table.CheckConstraint("CK_InventoryAuditItems_Result", "\"Result\" IN (\r\n    'Pending',\r\n    'Verified',\r\n    'Missing',\r\n    'Discrepancy'\r\n)");
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_EvidenceItems_EvidenceItemId",
                        column: x => x.EvidenceItemId,
                        principalTable: "EvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_FoundPropertyItems_FoundPropertyItemId",
                        column: x => x.FoundPropertyItemId,
                        principalTable: "FoundPropertyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_InventoryAudits_InventoryAuditId",
                        column: x => x.InventoryAuditId,
                        principalTable: "InventoryAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_SafekeepingItems_SafekeepingItemId",
                        column: x => x.SafekeepingItemId,
                        principalTable: "SafekeepingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_StorageLocations_ExpectedStorageLocationId",
                        column: x => x.ExpectedStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAuditItems_Users_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_CheckedByUserId",
                table: "InventoryAuditItems",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_EvidenceItemId",
                table: "InventoryAuditItems",
                column: "EvidenceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_ExpectedStorageLocationId",
                table: "InventoryAuditItems",
                column: "ExpectedStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_FoundPropertyItemId",
                table: "InventoryAuditItems",
                column: "FoundPropertyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_InventoryAuditId_EvidenceItemId",
                table: "InventoryAuditItems",
                columns: new[] { "InventoryAuditId", "EvidenceItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_InventoryAuditId_FoundPropertyItemId",
                table: "InventoryAuditItems",
                columns: new[] { "InventoryAuditId", "FoundPropertyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_InventoryAuditId_Result",
                table: "InventoryAuditItems",
                columns: new[] { "InventoryAuditId", "Result" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_InventoryAuditId_SafekeepingItemId",
                table: "InventoryAuditItems",
                columns: new[] { "InventoryAuditId", "SafekeepingItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditItems_SafekeepingItemId",
                table: "InventoryAuditItems",
                column: "SafekeepingItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAudits_CompletedByUserId",
                table: "InventoryAudits",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAudits_StartedByUserId",
                table: "InventoryAudits",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAudits_StorageLocationId_Status",
                table: "InventoryAudits",
                columns: new[] { "StorageLocationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAuditItems");

            migrationBuilder.DropTable(
                name: "InventoryAudits");
        }
    }
}
