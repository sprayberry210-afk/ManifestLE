using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBadgeNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeNumber",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadgeNumber",
                table: "Users");
        }
    }
}
