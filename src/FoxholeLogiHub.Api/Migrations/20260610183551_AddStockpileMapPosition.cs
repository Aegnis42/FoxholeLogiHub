using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockpileMapPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MapX",
                table: "Stockpiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MapY",
                table: "Stockpiles",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MapX",
                table: "Stockpiles");

            migrationBuilder.DropColumn(
                name: "MapY",
                table: "Stockpiles");
        }
    }
}
