using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoxholeLogiHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantResupplyAllianceToExistingRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historiquement, tout membre pouvait créer des demandes visibles par l'alliance :
            // les rôles existants reçoivent ResupplyAlliance (8192) pour ne rien perdre.
            // (Le bit 2048, déjà accordé, devient « Demandes (régiment) » — même sens.)
            migrationBuilder.Sql("UPDATE \"RegimentRoles\" SET \"Permissions\" = \"Permissions\" | 8192;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"RegimentRoles\" SET \"Permissions\" = \"Permissions\" & ~8192;");
        }
    }
}
