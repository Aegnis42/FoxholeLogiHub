namespace FoxholeLogiHub.Core.Models;

/// <summary>
/// Compte de l'application, identifié par le Steam ID. Stocké localement
/// (%APPDATA%\FoxholeLogiHub\account.json). Le pseudo est modifiable par l'utilisateur ;
/// faction et avatar sont rafraîchis depuis les sources (sauvegarde + cache Steam).
/// </summary>
public sealed class Account
{
    public string SteamId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Faction Faction { get; set; }
    public string? AvatarPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
