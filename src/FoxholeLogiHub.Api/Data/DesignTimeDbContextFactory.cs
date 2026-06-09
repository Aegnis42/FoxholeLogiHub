using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoxholeLogiHub.Api.Data;

/// <summary>
/// Utilisée uniquement par les outils EF (dotnet ef migrations…) pour générer des migrations
/// ciblant PostgreSQL. La connexion n'est pas réellement ouverte lors de la génération.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=foxhole;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options);
    }
}
