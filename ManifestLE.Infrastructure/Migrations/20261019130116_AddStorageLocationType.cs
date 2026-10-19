using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageLocationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LocationType",
                table: "StorageLocations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationType",
                table: "StorageLocations");
        }
    }
}
