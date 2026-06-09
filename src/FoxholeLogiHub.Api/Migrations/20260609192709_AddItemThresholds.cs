using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddItemThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CriticalThreshold",
                table: "StockpileItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LowThreshold",
                table: "StockpileItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriticalThreshold",
                table: "StockpileItems");

            migrationBuilder.DropColumn(
                name: "LowThreshold",
                table: "StockpileItems");
        }
    }
}
