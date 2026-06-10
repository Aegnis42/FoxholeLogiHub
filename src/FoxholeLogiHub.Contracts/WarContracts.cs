namespace FoxholeLogiHub.Contracts;

/// <summary>Contrôle de la ville d'un stockpile, relatif à la faction du régiment.</summary>
public static class WarTownControl
{
    public const string Friendly = "friendly"; // ville tenue par notre faction
    public const string Enemy = "enemy";       // ville aux mains de l'ennemi → stockpile menacé/perdu
    public const string Neutral = "neutral";   // ville neutre
    public const string Unknown = "unknown";   // ville/hex non reconnu ou données War indisponibles
}

/// <summary>État de la guerre en cours (API publique Foxhole, mise en cache côté serveur).</summary>
public sealed record WarStatusDto(
    bool Available,
    int WarNumber,
    int DayOfWar,
    string Winner,
    int RequiredVictoryTowns,
    int WardenVictoryTowns,
    int ColonialVictoryTowns);

/// <summary>Résultat du reset de fin de guerre (données du régiment purgées).</summary>
public sealed record WarResetResultDto(int Stockpiles, int Items, int Requests);
