using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.Api.War;

public static class WarEndpoints
{
    // Cache du DTO carte, lié au snapshot courant par référence (champ référence : écriture atomique).
    private sealed record MapDtoCache(object? Snapshot, WarMapDto Dto);
    private static volatile MapDtoCache _mapDtoCache = new(null, new WarMapDto(false, new List<WarMapHexDto>()));

    public static void MapWarEndpoints(this WebApplication app)
    {
        // État de la guerre (depuis le cache — jamais d'appel direct à l'API publique ici).
        app.MapGet("/api/war", (WarStateService state) =>
        {
            var snap = state.Current;
            if (snap is null)
                return Results.Ok(new WarStatusDto(false, 0, 0, "NONE", 0, 0, 0));

            int day = (int)Math.Max(1, (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(snap.Info.ConquestStartTime)).TotalDays + 1);
            return Results.Ok(new WarStatusDto(
                true, snap.Info.WarNumber, day, snap.Info.Winner,
                snap.Info.RequiredVictoryTowns, snap.WardenVictoryTowns, snap.ColonialVictoryTowns));
        }).RequireAuthorization();

        // Carte du monde : contrôle des villes par hexagone (positions relatives incluses).
        // Le DTO est construit UNE fois par snapshot (le cache War ne change que toutes les 5 min)
        // au lieu de re-projeter ~430 villes + ~1500 structures à chaque requête.
        app.MapGet("/api/war/map", (WarStateService state) =>
        {
            var snap = state.Current;
            if (snap is null)
                return Results.Ok(new WarMapDto(false, new List<WarMapHexDto>()));

            var cached = _mapDtoCache;
            if (!ReferenceEquals(cached.Snapshot, snap))
            {
                var hexes = snap.TownsByHex.Values
                    .Where(towns => towns.Count > 0)
                    .Select(towns => new WarMapHexDto(
                        towns[0].Map,
                        towns.Select(t => new WarMapTownDto(t.Town, t.X, t.Y, t.TeamId, t.Scorched, t.VictoryBase, t.Tier)).ToList(),
                        snap.StructuresByMap.TryGetValue(towns[0].Map, out var structs)
                            ? structs.Select(s => new WarMapStructDto(s.IconType, s.X, s.Y, s.TeamId)).ToList()
                            : new List<WarMapStructDto>()))
                    .ToList();
                cached = new MapDtoCache(snap, new WarMapDto(true, hexes));
                _mapDtoCache = cached; // course bénigne : deux threads construiraient le même DTO
            }
            return Results.Ok(cached.Dto);
        }).RequireAuthorization();
    }
}
