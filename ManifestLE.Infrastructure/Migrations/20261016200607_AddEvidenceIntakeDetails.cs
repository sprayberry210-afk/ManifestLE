using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceIntakeDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectionLocation",
                table: "EvidenceItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "EvidenceItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerContact",
                table: "EvidenceItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "EvidenceItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Packaging",
                table: "EvidenceItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectionLocation",
                table: "EvidenceItems");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "EvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnerContact",
                table: "EvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "EvidenceItems");

            migrationBuilder.DropColumn(
                name: "Packaging",
                table: "EvidenceItems");
        }
    }
}
