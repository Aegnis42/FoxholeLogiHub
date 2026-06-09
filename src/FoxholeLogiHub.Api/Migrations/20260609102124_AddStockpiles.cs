using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockpiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stockpiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Hex = table.Column<string>(type: "text", nullable: false),
                    Town = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBySteamId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stockpiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockpileShares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockpileId = table.Column<string>(type: "text", nullable: false),
                    RegimentId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockpileShares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stockpiles_RegimentId",
                table: "Stockpiles",
                column: "RegimentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockpileShares_StockpileId_RegimentId",
                table: "StockpileShares",
                columns: new[] { "StockpileId", "RegimentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stockpiles");

            migrationBuilder.DropTable(
                name: "StockpileShares");
        }
    }
}
