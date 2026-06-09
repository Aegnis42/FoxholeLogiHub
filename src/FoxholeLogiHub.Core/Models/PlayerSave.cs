namespace FoxholeLogiHub.Core.Models;

/// <summary>Faction du joueur dans Foxhole.</summary>
public enum Faction
{
    Unknown = 0,
    Wardens,
    Colonials,
}

/// <summary>Un item d'un loadout (équipement ou sac à dos).</summary>
public sealed class LoadoutItem
{
    /// <summary>Nom de code interne du jeu, ex. "WorkHammer", "Shovel".</summary>
    public required string CodeName { get; init; }
    public int Quantity { get; init; }
    /// <summary>Slot d'équipement, ex. "Body", "Primary". Null pour le sac à dos.</summary>
    public string? Slot { get; init; }
}

/// <summary>Un loadout sauvegardé par le joueur.</summary>
public sealed class Loadout
{
    public required string Name { get; init; }
    public List<LoadoutItem> Equipment { get; } = new();
    public List<LoadoutItem> Backpack { get; } = new();
}

/// <summary>
/// Données joueur extraites du fichier de sauvegarde local Foxhole (&lt;steamid&gt;.sav).
/// Lecture seule : on ne modifie jamais le fichier du jeu.
/// </summary>
public sealed class PlayerSave
{
    public string? SteamId { get; init; }
    public Faction Faction { get; init; }
    public string? LastServer { get; init; }
    public int LastShardId { get; init; }
    public string? Language { get; init; }
    public List<string> WarsJoined { get; init; } = new();
    public List<Loadout> Loadouts { get; init; } = new();
}
