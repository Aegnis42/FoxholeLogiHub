using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegimentRoleTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordRoleTag",
                table: "Regiments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscordRoleTag",
                table: "Regiments");
        }
    }
}
