using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Data;

public sealed class User
{
    public string SteamId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Faction { get; set; } = "Unknown";
    public string FriendCode { get; set; } = "";
    /// <summary>Avatar PNG partagé (octets bruts), null si non envoyé.</summary>
    public byte[]? AvatarPng { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>Relation d'amitié dirigée. Une amitié mutuelle = deux lignes (A→B et B→A).</summary>
public sealed class Friendship
{
    public int Id { get; set; }
    public string UserSteamId { get; set; } = "";
    public string FriendSteamId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Demande d'ami en attente (FromSteamId veut ajouter ToSteamId).</summary>
public sealed class FriendRequest
{
    public int Id { get; set; }
    public string FromSteamId { get; set; } = "";
    public string ToSteamId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(u => u.SteamId);
            e.HasIndex(u => u.FriendCode).IsUnique();
        });

        b.Entity<Friendship>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => new { f.UserSteamId, f.FriendSteamId }).IsUnique();
        });

        b.Entity<FriendRequest>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => new { f.FromSteamId, f.ToSteamId }).IsUnique();
        });
    }
}
