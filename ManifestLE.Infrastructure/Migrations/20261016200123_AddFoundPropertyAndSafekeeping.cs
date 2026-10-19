using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundPropertyAndSafekeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "EvidenceItemId",
                table: "CustodyEvents",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "FoundPropertyItemId",
                table: "CustodyEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SafekeepingItemId",
                table: "CustodyEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FoundPropertyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CollectedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FoundLocation = table.Column<string>(type: "TEXT", nullable: false),
                    FinderName = table.Column<string>(type: "TEXT", nullable: false),
                    FinderContact = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerName = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerContact = table.Column<string>(type: "TEXT", nullable: false),
                    Packaging = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentStorageLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundPropertyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoundPropertyItems_StorageLocations_CurrentStorageLocationId",
                        column: x => x.CurrentStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafekeepingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CollectedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CollectionLocation = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerName = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerContact = table.Column<string>(type: "TEXT", nullable: false),
                    HoldReason = table.Column<string>(type: "TEXT", nullable: false),
                    HoldAuthority = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewDueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsFirearm = table.Column<bool>(type: "INTEGER", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Caliber = table.Column<string>(type: "TEXT", nullable: false),
                    Packaging = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentStorageLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafekeepingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafekeepingItems_StorageLocations_CurrentStorageLocationId",
                        column: x => x.CurrentStorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_FoundPropertyItemId",
                table: "CustodyEvents",
                column: "FoundPropertyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_SafekeepingItemId",
                table: "CustodyEvents",
                column: "SafekeepingItemId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustodyEvents_ExactlyOneItem",
                table: "CustodyEvents",
                sql: "(\r\n    CASE WHEN \"EvidenceItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"FoundPropertyItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"SafekeepingItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FoundPropertyItems_CurrentStorageLocationId",
                table: "FoundPropertyItems",
                column: "CurrentStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_FoundPropertyItems_ReportNumber_ItemNumber",
                table: "FoundPropertyItems",
                columns: new[] { "ReportNumber", "ItemNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafekeepingItems_CurrentStorageLocationId",
                table: "SafekeepingItems",
                column: "CurrentStorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SafekeepingItems_ReportNumber_ItemNumber",
                table: "SafekeepingItems",
                columns: new[] { "ReportNumber", "ItemNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustodyEvents_FoundPropertyItems_FoundPropertyItemId",
                table: "CustodyEvents",
                column: "FoundPropertyItemId",
                principalTable: "FoundPropertyItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustodyEvents_SafekeepingItems_SafekeepingItemId",
                table: "CustodyEvents",
                column: "SafekeepingItemId",
                principalTable: "SafekeepingItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustodyEvents_FoundPropertyItems_FoundPropertyItemId",
                table: "CustodyEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_CustodyEvents_SafekeepingItems_SafekeepingItemId",
                table: "CustodyEvents");

            migrationBuilder.DropTable(
                name: "FoundPropertyItems");

            migrationBuilder.DropTable(
                name: "SafekeepingItems");

            migrationBuilder.DropIndex(
                name: "IX_CustodyEvents_FoundPropertyItemId",
                table: "CustodyEvents");

            migrationBuilder.DropIndex(
                name: "IX_CustodyEvents_SafekeepingItemId",
                table: "CustodyEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustodyEvents_ExactlyOneItem",
                table: "CustodyEvents");

            migrationBuilder.DropColumn(
                name: "FoundPropertyItemId",
                table: "CustodyEvents");

            migrationBuilder.DropColumn(
                name: "SafekeepingItemId",
                table: "CustodyEvents");

            migrationBuilder.AlterColumn<Guid>(
                name: "EvidenceItemId",
                table: "CustodyEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
