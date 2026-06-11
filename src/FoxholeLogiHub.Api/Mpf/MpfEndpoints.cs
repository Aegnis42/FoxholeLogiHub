using FoxholeLogiHub.Api.Common;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static FoxholeLogiHub.Api.Common.RegimentGuards;
using ClaimsPrincipal = System.Security.Claims.ClaimsPrincipal;

namespace FoxholeLogiHub.Api.Mpf;

/// <summary>
/// File MPF du régiment : chaque membre déclare ses commandes en cours (item, caisses, hexagone,
/// temps restant lu en jeu) ; tout le monde voit les comptes à rebours, et le webhook Discord
/// prévient quand c'est prêt (MpfWatcherService).
/// </summary>
public static class MpfEndpoints
{
    private const int MaxOrdersPerRegiment = 100;

    public static void MapMpfEndpoints(this WebApplication app)
    {
        app.MapGet("/api/mpf", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.Ok(new List<MpfOrderDto>());
            string regId = ctx.Value.reg.Id;
            bool canManage = await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles);

            var rows = await db.MpfOrders.AsNoTracking()
                .Where(o => o.RegimentId == regId)
                .OrderBy(o => o.DoneAtUnixMs)
                .ToListAsync();
            return Results.Ok(rows.Select(o => new MpfOrderDto(
                o.Id, o.ItemCode, o.ItemName, o.Crates, o.Hex,
                DateTimeOffset.FromUnixTimeMilliseconds(o.DoneAtUnixMs),
                o.CreatedByName,
                canManage || o.CreatedBySteamId == me)).ToList());
        }).RequireAuthorization();

        app.MapPost("/api/mpf", async (CreateMpfOrderRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.BadRequest(new ApiError("Rejoins un régiment d'abord."));
            if (string.IsNullOrWhiteSpace(req.ItemName))
                return Results.BadRequest(new ApiError("Item requis."));
            if (req.Crates is <= 0 or > 100)
                return Results.BadRequest(new ApiError("Nombre de caisses invalide (1-100)."));
            if (req.RemainingMinutes is < 0 or > 48 * 60)
                return Results.BadRequest(new ApiError("Temps restant invalide (0 à 48 h)."));
            if (await db.MpfOrders.CountAsync(o => o.RegimentId == ctx.Value.reg.Id) >= MaxOrdersPerRegiment)
                return Results.BadRequest(new ApiError("Trop de commandes MPF en cours — récupère ou supprime les anciennes."));

            string myName = await db.Users.AsNoTracking()
                .Where(u => u.SteamId == me).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "?";

            db.MpfOrders.Add(new MpfOrder
            {
                Id = Guid.NewGuid().ToString("N"),
                RegimentId = ctx.Value.reg.Id,
                ItemCode = Validate.Str(req.ItemCode ?? "", 64),
                ItemName = Validate.Str(req.ItemName, 96),
                Crates = req.Crates,
                Hex = Validate.Str(req.Hex ?? "", 48),
                DoneAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(req.RemainingMinutes).ToUnixTimeMilliseconds(),
                Notified = req.RemainingMinutes == 0, // déjà prête → pas de webhook rétroactif
                CreatedBySteamId = me,
                CreatedByName = myName,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.StockpilesChanged);
            return Results.Ok();
        }).RequireAuthorization();

        // « Récupéré » et suppression : créateur ou ManageStockpiles.
        app.MapPost("/api/mpf/collect", (MpfActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            RemoveAsync(req.Id, p, db, hub)).RequireAuthorization();
        app.MapPost("/api/mpf/delete", (MpfActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            RemoveAsync(req.Id, p, db, hub)).RequireAuthorization();
    }

    private static async Task<IResult> RemoveAsync(string id, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub)
    {
        string me = Me(p);
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return Results.Forbid();
        var order = await db.MpfOrders.FirstOrDefaultAsync(o => o.Id == id && o.RegimentId == ctx.Value.reg.Id);
        if (order is null)
            return Results.NotFound(new ApiError("Commande introuvable."));
        if (order.CreatedBySteamId != me
            && !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
            return Results.Forbid();

        db.MpfOrders.Remove(order);
        await db.SaveChangesAsync();
        await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.StockpilesChanged);
        return Results.Ok();
    }
}

/// <summary>
/// Vérifie chaque minute les commandes MPF arrivées à terme et prévient le régiment via son
/// webhook Discord (une seule fois par commande — flag Notified).
/// </summary>
public sealed class MpfWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DiscordNotifier _discord;
    private readonly ILogger<MpfWatcherService> _logger;

    public MpfWatcherService(IServiceScopeFactory scopes, DiscordNotifier discord, ILogger<MpfWatcherService> logger)
    {
        _scopes = scopes;
        _discord = discord;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Balayage MPF échoué.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task CheckAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var due = await db.MpfOrders
            .Where(o => !o.Notified && o.DoneAtUnixMs <= nowMs)
            .OrderBy(o => o.RegimentId)
            .Take(50)
            .ToListAsync();
        if (due.Count == 0)
            return;

        var regIds = due.Select(o => o.RegimentId).Distinct().ToList();
        var webhooks = await db.Regiments.AsNoTracking()
            .Where(r => regIds.Contains(r.Id) && r.DiscordWebhookUrl != "")
            .ToDictionaryAsync(r => r.Id, r => r.DiscordWebhookUrl);

        foreach (var group in due.GroupBy(o => o.RegimentId))
        {
            if (webhooks.TryGetValue(group.Key, out var url))
            {
                var lines = group.Take(8).Select(o =>
                    $"• {DiscordNotifier.Safe(o.ItemName)} ×{o.Crates} caisse(s)"
                    + (o.Hex.Length > 0 ? $" — {DiscordNotifier.Safe(o.Hex)}" : "")
                    + $" (par {DiscordNotifier.Safe(o.CreatedByName)})");
                _discord.Send(url, "🏭 **MPF terminé — à récupérer :**\n" + string.Join("\n", lines));
            }
            foreach (var o in group)
                o.Notified = true;
        }
        await db.SaveChangesAsync();
    }
}
