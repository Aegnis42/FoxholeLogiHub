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

public sealed class Regiment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Faction { get; set; } = "Unknown";
    public string InviteCode { get; set; } = "";
    public string OwnerSteamId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RegimentRole
{
    public int Id { get; set; }
    public string RegimentId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Permissions { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>Un joueur appartient à au plus un régiment (SteamId unique).</summary>
public sealed class RegimentMember
{
    public int Id { get; set; }
    public string RegimentId { get; set; } = "";
    public string SteamId { get; set; } = "";
    public int RoleId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>Invitation d'un ami à rejoindre un régiment (en attente).</summary>
public sealed class RegimentInvite
{
    public int Id { get; set; }
    public string RegimentId { get; set; } = "";
    public string ToSteamId { get; set; } = "";
    public string FromSteamId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Alliance entre deux régiments. ProposedByRegimentId distingue le proposant.</summary>
public sealed class RegimentAlliance
{
    public int Id { get; set; }
    public string RegimentAId { get; set; } = "";
    public string RegimentBId { get; set; } = "";
    public string ProposedByRegimentId { get; set; } = "";
    public bool Accepted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Un stockpile d'un régiment, lié à un hexagone (région) et un type de structure.</summary>
public sealed class Stockpile
{
    public string Id { get; set; } = "";
    public string RegimentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "";
    public string Town { get; set; } = "";
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";   // mot de passe (Port/Dépôt seulement)
    public bool IsPublic { get; set; }
    public string CreatedBySteamId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Partage d'un stockpile privé avec un régiment allié.</summary>
public sealed class StockpileShare
{
    public int Id { get; set; }
    public string StockpileId { get; set; } = "";
    public string RegimentId { get; set; } = "";
}

/// <summary>Un item (quantité) dans un stockpile, avec seuils d'alerte (0 = pas d'alerte).</summary>
public sealed class StockpileItem
{
    public int Id { get; set; }
    public string StockpileId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Quantity { get; set; }
    public int LowThreshold { get; set; }      // sous ce seuil → Bas
    public int CriticalThreshold { get; set; } // sous ce seuil → Critique
}

/// <summary>Une demande de ravitaillement d'un régiment (item + quantité, stockpile cible optionnel).</summary>
public sealed class ResupplyRequest
{
    public string Id { get; set; } = "";
    public string RegimentId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Quantity { get; set; }
    public string StockpileId { get; set; } = "";   // "" = non précisé
    public int Priority { get; set; }                // 0 normale, 1 haute, 2 urgente
    public string Status { get; set; } = "open";     // open | claimed | done
    public string Note { get; set; } = "";
    public string CreatedBySteamId { get; set; } = "";
    public string ClaimedBySteamId { get; set; } = ""; // "" = personne
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Regiment> Regiments => Set<Regiment>();
    public DbSet<RegimentRole> RegimentRoles => Set<RegimentRole>();
    public DbSet<RegimentMember> RegimentMembers => Set<RegimentMember>();
    public DbSet<RegimentInvite> RegimentInvites => Set<RegimentInvite>();
    public DbSet<RegimentAlliance> RegimentAlliances => Set<RegimentAlliance>();
    public DbSet<Stockpile> Stockpiles => Set<Stockpile>();
    public DbSet<StockpileShare> StockpileShares => Set<StockpileShare>();
    public DbSet<StockpileItem> StockpileItems => Set<StockpileItem>();
    public DbSet<ResupplyRequest> ResupplyRequests => Set<ResupplyRequest>();

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

        b.Entity<Regiment>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.InviteCode).IsUnique();
        });

        b.Entity<RegimentRole>(e => e.HasKey(r => r.Id));

        b.Entity<RegimentMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.SteamId).IsUnique(); // un seul régiment par joueur
        });

        b.Entity<RegimentInvite>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => new { i.RegimentId, i.ToSteamId }).IsUnique();
        });

        b.Entity<RegimentAlliance>(e => e.HasKey(a => a.Id));

        b.Entity<Stockpile>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.RegimentId);
        });

        b.Entity<StockpileShare>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.StockpileId, s.RegimentId }).IsUnique();
        });

        b.Entity<StockpileItem>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.StockpileId, s.Code }).IsUnique();
        });

        b.Entity<ResupplyRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.RegimentId);
        });
    }
}
