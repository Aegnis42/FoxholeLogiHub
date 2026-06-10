using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.Api.War;

public static class WarEndpoints
{
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
        app.MapGet("/api/war/map", (WarStateService state) =>
        {
            var snap = state.Current;
            if (snap is null)
                return Results.Ok(new WarMapDto(false, new List<WarMapHexDto>()));

            var hexes = snap.TownsByHex.Values
                .Where(towns => towns.Count > 0)
                .Select(towns => new WarMapHexDto(
                    towns[0].Map,
                    towns.Select(t => new WarMapTownDto(t.Town, t.X, t.Y, t.TeamId, t.Scorched, t.VictoryBase, t.Tier)).ToList(),
                    snap.StructuresByMap.TryGetValue(towns[0].Map, out var structs)
                        ? structs.Select(s => new WarMapStructDto(s.IconType, s.X, s.Y, s.TeamId)).ToList()
                        : new List<WarMapStructDto>()))
                .ToList();
            return Results.Ok(new WarMapDto(true, hexes));
        }).RequireAuthorization();
    }
}
