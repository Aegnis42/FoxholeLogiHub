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
    }
}
