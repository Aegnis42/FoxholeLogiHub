using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMpfAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MpfOrders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    Crates = table.Column<int>(type: "integer", nullable: false),
                    Hex = table.Column<string>(type: "text", nullable: false),
                    DoneAtUnixMs = table.Column<long>(type: "bigint", nullable: false),
                    Notified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBySteamId = table.Column<string>(type: "text", nullable: false),
                    CreatedByName = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUnixMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MpfOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    FromStockpileId = table.Column<string>(type: "text", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: false),
                    ToStockpileId = table.Column<string>(type: "text", nullable: false),
                    ToName = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    BySteamId = table.Column<string>(type: "text", nullable: false),
                    AtUnixMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MpfOrders_Notified_DoneAtUnixMs",
                table: "MpfOrders",
                columns: new[] { "Notified", "DoneAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_MpfOrders_RegimentId",
                table: "MpfOrders",
                column: "RegimentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_RegimentId_AtUnixMs",
                table: "StockTransfers",
                columns: new[] { "RegimentId", "AtUnixMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MpfOrders");

            migrationBuilder.DropTable(
                name: "StockTransfers");
        }
    }
}
