using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegimentAlliances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegimentAId = table.Column<string>(type: "text", nullable: false),
                    RegimentBId = table.Column<string>(type: "text", nullable: false),
                    ProposedByRegimentId = table.Column<string>(type: "text", nullable: false),
                    Accepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegimentAlliances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegimentInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    ToSteamId = table.Column<string>(type: "text", nullable: false),
                    FromSteamId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegimentInvites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegimentMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    SteamId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegimentMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegimentRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegimentId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Permissions = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegimentRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regiments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    Faction = table.Column<string>(type: "text", nullable: false),
                    InviteCode = table.Column<string>(type: "text", nullable: false),
                    OwnerSteamId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regiments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegimentInvites_RegimentId_ToSteamId",
                table: "RegimentInvites",
                columns: new[] { "RegimentId", "ToSteamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegimentMembers_SteamId",
                table: "RegimentMembers",
                column: "SteamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regiments_InviteCode",
                table: "Regiments",
                column: "InviteCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegimentAlliances");

            migrationBuilder.DropTable(
                name: "RegimentInvites");

            migrationBuilder.DropTable(
                name: "RegimentMembers");

            migrationBuilder.DropTable(
                name: "RegimentRoles");

            migrationBuilder.DropTable(
                name: "Regiments");
        }
    }
}
