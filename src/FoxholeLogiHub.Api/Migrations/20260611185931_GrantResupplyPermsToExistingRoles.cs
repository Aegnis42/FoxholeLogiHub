using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantResupplyPermsToExistingRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Avant les permissions granulaires de ravitaillement, créer/livrer/rouvrir était
            // ouvert à tous les membres : on préserve ce comportement en accordant
            // ResupplyCreate (2048) + ResupplyManage (4096) à tous les rôles existants.
            // Les chefs peuvent ensuite retirer ces cases rôle par rôle.
            migrationBuilder.Sql("UPDATE \"RegimentRoles\" SET \"Permissions\" = \"Permissions\" | 6144;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"RegimentRoles\" SET \"Permissions\" = \"Permissions\" & ~6144;");
        }
    }
}
