using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReworkResupplyMultiItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ResupplyRequests");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "ResupplyRequests");

            migrationBuilder.RenameColumn(
                name: "StockpileId",
                table: "ResupplyRequests",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ResupplyRequests",
                newName: "Hex");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "ResupplyRequests",
                newName: "Coords");

            migrationBuilder.CreateTable(
                name: "ResupplyRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResupplyRequestItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResupplyRequestItems_RequestId",
                table: "ResupplyRequestItems",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResupplyRequestItems");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "ResupplyRequests",
                newName: "StockpileId");

            migrationBuilder.RenameColumn(
                name: "Hex",
                table: "ResupplyRequests",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "Coords",
                table: "ResupplyRequests",
                newName: "Code");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ResupplyRequests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "ResupplyRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
