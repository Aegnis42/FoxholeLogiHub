using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryTemplatesWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordWebhookUrl",
                table: "Regiments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "StockpileItemSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockpileId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    TakenAtUnixMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockpileItemSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockpileTemplateItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    LowThreshold = table.Column<int>(type: "integer", nullable: false),
                    CriticalThreshold = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockpileTemplateItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockpileTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedBySteamId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockpileTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockpileItemSnapshots_StockpileId_TakenAtUnixMs",
                table: "StockpileItemSnapshots",
                columns: new[] { "StockpileId", "TakenAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTemplateItems_TemplateId",
                table: "StockpileTemplateItems",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StockpileTemplates_RegimentId",
                table: "StockpileTemplates",
                column: "RegimentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockpileItemSnapshots");

            migrationBuilder.DropTable(
                name: "StockpileTemplateItems");

            migrationBuilder.DropTable(
                name: "StockpileTemplates");

            migrationBuilder.DropColumn(
                name: "DiscordWebhookUrl",
                table: "Regiments");
        }
    }
}
