using System.Security.Claims;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Stockpiles;

public static class StockpileEndpoints
{
    public static void MapStockpileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stockpiles", async (ClaimsPrincipal p, AppDbContext db) =>
            Results.Ok(await BuildListAsync(db, Me(p)))).RequireAuthorization();

        app.MapPost("/api/stockpiles", async (CreateStockpileRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.BadRequest(new ApiError("Rejoins ou crée un régiment d'abord."));
            if (!await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Hex) || string.IsNullOrWhiteSpace(req.Type))
                return Results.BadRequest(new ApiError("Nom, hexagone et type requis."));

            var now = DateTimeOffset.UtcNow;
            db.Stockpiles.Add(new Stockpile
            {
                Id = Guid.NewGuid().ToString("N"),
                RegimentId = ctx.Value.reg.Id,
                Name = req.Name.Trim(),
                Hex = req.Hex.Trim(),
                Town = (req.Town ?? "").Trim(),
                Type = req.Type,
                Code = StockpileTypes.UsesCode(req.Type) ? (req.Code ?? "").Trim() : "",
                IsPublic = req.IsPublic,
                CreatedBySteamId = me,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        app.MapPut("/api/stockpiles", async (UpdateStockpileRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var s = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.Id && x.RegimentId == ctx.Value.reg.Id);
            if (s is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));

            s.Name = req.Name.Trim();
            s.Hex = req.Hex.Trim();
            s.Town = (req.Town ?? "").Trim();
            s.Type = req.Type;
            s.Code = StockpileTypes.UsesCode(req.Type) ? (req.Code ?? "").Trim() : "";
            s.IsPublic = req.IsPublic;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        app.MapPost("/api/stockpiles/delete", async (DeleteStockpileRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var s = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.Id && x.RegimentId == ctx.Value.reg.Id);
            if (s is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));

            db.StockpileShares.RemoveRange(db.StockpileShares.Where(sh => sh.StockpileId == s.Id));
            db.Stockpiles.Remove(s);
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        app.MapPost("/api/stockpiles/share", async (ShareStockpileRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var s = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.StockpileId && x.RegimentId == ctx.Value.reg.Id);
            if (s is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));

            var allies = await AlliedIdsAsync(db, ctx.Value.reg.Id);
            if (!allies.Contains(req.RegimentId))
                return Results.BadRequest(new ApiError("Ce régiment n'est pas un allié."));

            if (!await db.StockpileShares.AnyAsync(sh => sh.StockpileId == s.Id && sh.RegimentId == req.RegimentId))
            {
                db.StockpileShares.Add(new StockpileShare { StockpileId = s.Id, RegimentId = req.RegimentId });
                await db.SaveChangesAsync();
            }
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            await NotifyAsync(hub, db, req.RegimentId);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        app.MapPost("/api/stockpiles/unshare", async (UnshareStockpileRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var s = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.StockpileId && x.RegimentId == ctx.Value.reg.Id);
            if (s is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));

            db.StockpileShares.RemoveRange(db.StockpileShares.Where(sh => sh.StockpileId == s.Id && sh.RegimentId == req.RegimentId));
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            await NotifyAsync(hub, db, req.RegimentId);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        // --- Contenu (items) ---

        app.MapGet("/api/stockpiles/{id}/items", async (string id, ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.Ok(new List<StockpileItemDto>());
            var sp = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == id);
            if (sp is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));
            if (!await CanSeeAsync(db, sp, ctx.Value.reg.Id))
                return Results.Forbid();
            return Results.Ok(await ItemsAsync(db, id));
        }).RequireAuthorization();

        app.MapPost("/api/stockpiles/items/set", async (SetStockpileItemRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var sp = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.StockpileId && x.RegimentId == ctx.Value.reg.Id);
            if (sp is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));
            string code = (req.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new ApiError("Item requis."));

            var item = await db.StockpileItems.FirstOrDefaultAsync(i => i.StockpileId == sp.Id && i.Code == code);
            if (req.Quantity <= 0)
            {
                if (item is not null)
                    db.StockpileItems.Remove(item);
            }
            else if (item is null)
            {
                db.StockpileItems.Add(new StockpileItem
                {
                    StockpileId = sp.Id, Code = code,
                    Name = string.IsNullOrWhiteSpace(req.Name) ? code : req.Name.Trim(),
                    Category = (req.Category ?? "").Trim(), Quantity = req.Quantity,
                    LowThreshold = Math.Max(0, req.LowThreshold), CriticalThreshold = Math.Max(0, req.CriticalThreshold),
                });
            }
            else
            {
                item.Quantity = req.Quantity;
                item.Name = string.IsNullOrWhiteSpace(req.Name) ? code : req.Name.Trim();
                item.Category = (req.Category ?? "").Trim();
                item.LowThreshold = Math.Max(0, req.LowThreshold);
                item.CriticalThreshold = Math.Max(0, req.CriticalThreshold);
            }

            sp.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, sp.RegimentId);
            return Results.Ok(await ItemsAsync(db, sp.Id));
        }).RequireAuthorization();

        // Remplace tout le contenu (import auto / capture).
        app.MapPost("/api/stockpiles/items/import", async (ImportStockpileItemsRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
                return Results.Forbid();
            var sp = await db.Stockpiles.FirstOrDefaultAsync(x => x.Id == req.StockpileId && x.RegimentId == ctx.Value.reg.Id);
            if (sp is null)
                return Results.NotFound(new ApiError("Stockpile introuvable."));

            // Préserve les seuils d'alerte existants (par code) — un ré-import ne doit pas les effacer.
            var existingItems = await db.StockpileItems.Where(i => i.StockpileId == sp.Id).ToListAsync();
            var thresholds = existingItems
                .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.First().LowThreshold, g.First().CriticalThreshold), StringComparer.OrdinalIgnoreCase);
            db.StockpileItems.RemoveRange(existingItems);

            // Dédoublonne par code (FIR peut renvoyer le même item plusieurs fois) → somme.
            var deduped = req.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Code) && i.Quantity > 0)
                .GroupBy(i => i.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    thresholds.TryGetValue(g.Key, out var th);
                    return new StockpileItem
                    {
                        StockpileId = sp.Id,
                        Code = g.Key,
                        Name = string.IsNullOrWhiteSpace(first.Name) ? g.Key : first.Name.Trim(),
                        Category = (first.Category ?? "").Trim(),
                        Quantity = g.Sum(x => x.Quantity),
                        LowThreshold = th.LowThreshold,
                        CriticalThreshold = th.CriticalThreshold,
                    };
                });
            db.StockpileItems.AddRange(deduped);
            sp.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, sp.RegimentId);
            return Results.Ok(await ItemsAsync(db, sp.Id));
        }).RequireAuthorization();

        // --- Tableau de bord : alertes de stock (items sous seuil) sur tous les stockpiles visibles ---
        app.MapGet("/api/stockpiles/alerts", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.Ok(new List<StockpileAlertDto>());
            string myRegId = ctx.Value.reg.Id;

            // Pas d'alertes sur les stockpiles PUBLICS (dépôts partagés que tous remplissent) :
            // on ne suit que les réserves privées (les miennes + celles d'alliés partagées avec moi).
            var own = await db.Stockpiles.Where(s => s.RegimentId == myRegId && !s.IsPublic).ToListAsync();
            var alliedIds = await AlliedIdsAsync(db, myRegId);
            var allied = await db.Stockpiles
                .Where(s => alliedIds.Contains(s.RegimentId) && !s.IsPublic
                    && db.StockpileShares.Any(sh => sh.StockpileId == s.Id && sh.RegimentId == myRegId))
                .ToListAsync();
            var all = own.Concat(allied).ToList();
            var spById = all.ToDictionary(s => s.Id);
            var spIds = all.Select(s => s.Id).ToList();

            var regIds = all.Select(s => s.RegimentId).Distinct().ToList();
            var names = await db.Regiments.Where(r => regIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);

            var items = await db.StockpileItems
                .Where(i => spIds.Contains(i.StockpileId)
                    && ((i.CriticalThreshold > 0 && i.Quantity <= i.CriticalThreshold)
                     || (i.LowThreshold > 0 && i.Quantity <= i.LowThreshold)))
                .ToListAsync();

            var alerts = items.Select(i =>
            {
                var s = spById[i.StockpileId];
                bool crit = i.CriticalThreshold > 0 && i.Quantity <= i.CriticalThreshold;
                return new StockpileAlertDto(
                    s.Id, s.Name, names.GetValueOrDefault(s.RegimentId, "?"), s.RegimentId == myRegId,
                    s.Hex, s.Town, s.Type,
                    i.Code, i.Name, i.Category, i.Quantity, i.LowThreshold, i.CriticalThreshold,
                    crit ? "critical" : "low");
            })
            .OrderBy(a => a.Severity == "critical" ? 0 : 1).ThenBy(a => a.Name)
            .ToList();
            return Results.Ok(alerts);
        }).RequireAuthorization();
    }

    private static async Task<List<StockpileItemDto>> ItemsAsync(AppDbContext db, string stockpileId) =>
        await db.StockpileItems.Where(i => i.StockpileId == stockpileId)
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .Select(i => new StockpileItemDto(i.Code, i.Name, i.Category, i.Quantity, i.LowThreshold, i.CriticalThreshold))
            .ToListAsync();

    private static async Task<bool> CanSeeAsync(AppDbContext db, Stockpile sp, string myRegId)
    {
        if (sp.RegimentId == myRegId)
            return true;
        var allies = await AlliedIdsAsync(db, myRegId);
        if (!allies.Contains(sp.RegimentId))
            return false;
        if (sp.IsPublic)
            return true;
        return await db.StockpileShares.AnyAsync(s => s.StockpileId == sp.Id && s.RegimentId == myRegId);
    }

    private static async Task<List<StockpileDto>> BuildListAsync(AppDbContext db, string me)
    {
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return new List<StockpileDto>();

        string myRegId = ctx.Value.reg.Id;
        bool canManage = await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles);

        var own = await db.Stockpiles.Where(s => s.RegimentId == myRegId).ToListAsync();

        var alliedIds = await AlliedIdsAsync(db, myRegId);
        var allied = await db.Stockpiles
            .Where(s => alliedIds.Contains(s.RegimentId)
                && (s.IsPublic || db.StockpileShares.Any(sh => sh.StockpileId == s.Id && sh.RegimentId == myRegId)))
            .ToListAsync();

        var all = own.Concat(allied).ToList();
        var regIds = all.Select(s => s.RegimentId).Distinct().ToList();
        var names = await db.Regiments.Where(r => regIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);

        var ownIds = own.Select(o => o.Id).ToList();
        var shares = await db.StockpileShares.Where(sh => ownIds.Contains(sh.StockpileId)).ToListAsync();

        return all.Select(s =>
        {
            bool isOwn = s.RegimentId == myRegId;
            var sharedIds = isOwn
                ? shares.Where(sh => sh.StockpileId == s.Id).Select(sh => sh.RegimentId).ToList()
                : new List<string>();
            return new StockpileDto(s.Id, s.RegimentId, names.GetValueOrDefault(s.RegimentId, "?"),
                s.Name, s.Hex, s.Town, s.Type, s.Code, s.IsPublic, isOwn, isOwn && canManage, sharedIds);
        })
        .OrderByDescending(d => d.IsOwn).ThenBy(d => d.Hex).ThenBy(d => d.Name)
        .ToList();
    }

    private static async Task<List<string>> AlliedIdsAsync(AppDbContext db, string regId)
    {
        var rows = await db.RegimentAlliances
            .Where(a => a.Accepted && (a.RegimentAId == regId || a.RegimentBId == regId)).ToListAsync();
        return rows.Select(a => a.RegimentAId == regId ? a.RegimentBId : a.RegimentAId).Distinct().ToList();
    }

    private static async Task NotifyAsync(IHubContext<PresenceHub> hub, AppDbContext db, string regimentId)
    {
        var memberIds = await db.RegimentMembers.Where(m => m.RegimentId == regimentId).Select(m => m.SteamId).ToListAsync();
        if (memberIds.Count > 0)
            await hub.Clients.Users(memberIds).SendAsync(PresenceEvents.StockpilesChanged);
    }

    // --- Helpers (identité + appartenance + permission) ---

    private static string Me(ClaimsPrincipal p) =>
        p.FindFirstValue(TokenService.SteamIdClaim) ?? throw new InvalidOperationException("Jeton sans Steam ID.");

    private static async Task<(Regiment reg, RegimentMember member)?> MyRegimentAsync(AppDbContext db, string steamId)
    {
        var member = await db.RegimentMembers.FirstOrDefaultAsync(m => m.SteamId == steamId);
        if (member is null)
            return null;
        var reg = await db.Regiments.FirstOrDefaultAsync(r => r.Id == member.RegimentId);
        return reg is null ? null : (reg, member);
    }

    private static async Task<bool> HasPermAsync(AppDbContext db, Regiment reg, RegimentMember member, string steamId, RegimentPermission perm)
    {
        if (reg.OwnerSteamId == steamId)
            return true;
        var role = await db.RegimentRoles.FirstOrDefaultAsync(r => r.Id == member.RoleId);
        return role is not null && ((RegimentPermission)role.Permissions & perm) == perm;
    }
}
