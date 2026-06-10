using FoxholeLogiHub.Api.Common;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static FoxholeLogiHub.Api.Common.RegimentGuards;

namespace FoxholeLogiHub.Api.Resupply;

public static class ResupplyEndpoints
{
    /// <summary>
    /// Qui peut agir sur une demande : Own = créateur ou ManageStockpiles du régiment propriétaire
    /// (supprimer, changer la visibilité) ; OwnerOrClaimer = membre du régiment propriétaire OU
    /// preneur en charge (livré, rouvrir) — un inconnu ne peut pas clore la demande d'autrui.
    /// </summary>
    private enum Scope { Own, OwnerOrClaimer }

    public static void MapResupplyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/resupply", async (System.Security.Claims.ClaimsPrincipal p, AppDbContext db) =>
            Results.Ok(await BuildListAsync(db, Me(p)))).RequireAuthorization();

        app.MapPost("/api/resupply", async (CreateResupplyRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.BadRequest(new ApiError("Rejoins un régiment d'abord."));
            var items = (req.Items ?? new List<ResupplyItemDto>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Code) && i.Quantity > 0)
                .ToList();
            if (items.Count == 0)
                return Results.BadRequest(new ApiError("Ajoute au moins un item."));
            if (items.Count > Validate.MaxRequestItems)
                return Results.BadRequest(new ApiError($"Trop d'items ({items.Count}) — maximum {Validate.MaxRequestItems} par demande."));

            string id = Guid.NewGuid().ToString("N");
            db.ResupplyRequests.Add(new ResupplyRequest
            {
                Id = id,
                RegimentId = ctx.Value.reg.Id,
                Title = string.IsNullOrWhiteSpace(req.Title) ? "Demande" : Validate.Str(req.Title, 80),
                Hex = Validate.Str(req.Hex, 48),
                Coords = Validate.Str(req.Coords, 32),
                Priority = Math.Clamp(req.Priority, 0, 2),
                Visibility = Math.Clamp(req.Visibility, 0, 2),
                Status = ResupplyStatus.Open,
                Note = Validate.Str(req.Note, 500),
                CreatedBySteamId = me,
                ClaimedBySteamId = "",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            foreach (var it in items)
                db.ResupplyRequestItems.Add(new ResupplyRequestItem
                {
                    RequestId = id,
                    Code = Validate.Str(it.Code, 64),
                    Name = string.IsNullOrWhiteSpace(it.Name) ? Validate.Str(it.Code, 64) : Validate.Str(it.Name, 96),
                    Category = Validate.Str(it.Category, 48),
                    Quantity = Validate.Qty(it.Quantity),
                });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.ResupplyChanged);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        // Prendre en charge / se désengager. Toute demande visible peut être prise si personne ne
        // s'en occupe (collaboration inter-régiment) ; reprendre la prise d'un AUTRE joueur est
        // réservé au régiment propriétaire (anti-vol de prise en charge).
        app.MapPost("/api/resupply/claim", async (ResupplyActionRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.Forbid();
            string myRegId = ctx.Value.reg.Id;
            var r = await db.ResupplyRequests.FirstOrDefaultAsync(x => x.Id == req.Id);
            if (r is null)
                return Results.NotFound(new ApiError("Demande introuvable."));
            if (!await CanSeeAsync(db, r, myRegId))
                return Results.Forbid();

            if (r.ClaimedBySteamId == me)
            {
                r.ClaimedBySteamId = "";
                if (r.Status == ResupplyStatus.Claimed)
                    r.Status = ResupplyStatus.Open;
            }
            else if (r.ClaimedBySteamId.Length == 0)
            {
                r.ClaimedBySteamId = me;
                if (r.Status == ResupplyStatus.Open)
                    r.Status = ResupplyStatus.Claimed;
            }
            else if (r.RegimentId == myRegId)
            {
                r.ClaimedBySteamId = me; // le régiment propriétaire peut réattribuer
            }
            else
            {
                return Results.BadRequest(new ApiError("Déjà pris en charge par quelqu'un d'autre."));
            }

            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, r.RegimentId, PresenceEvents.ResupplyChanged);
            if (myRegId != r.RegimentId)
                await NotifyRegimentAsync(hub, db, myRegId, PresenceEvents.ResupplyChanged);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        // Marquer livrée (régiment propriétaire ou preneur en charge).
        app.MapPost("/api/resupply/done", async (ResupplyActionRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, Scope.OwnerOrClaimer, r => r.Status = ResupplyStatus.Done)).RequireAuthorization();

        // Rouvrir (régiment propriétaire ou preneur en charge).
        app.MapPost("/api/resupply/reopen", async (ResupplyActionRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, Scope.OwnerOrClaimer, r =>
            { r.Status = r.ClaimedBySteamId.Length > 0 ? ResupplyStatus.Claimed : ResupplyStatus.Open; })).RequireAuthorization();

        // Changer la visibilité (créateur ou ManageStockpiles, sur ses propres demandes).
        app.MapPost("/api/resupply/visibility", async (SetResupplyVisibilityRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, Scope.Own, r => r.Visibility = Math.Clamp(req.Visibility, 0, 2))).RequireAuthorization();

        // Supprimer (créateur ou ManageStockpiles, sur ses propres demandes).
        app.MapPost("/api/resupply/delete", async (ResupplyActionRequest req, System.Security.Claims.ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, Scope.Own, r =>
            {
                db.ResupplyRequestItems.RemoveRange(db.ResupplyRequestItems.Where(i => i.RequestId == r.Id));
                db.ResupplyRequests.Remove(r);
            })).RequireAuthorization();
    }

    private static async Task<IResult> MutateAsync(AppDbContext db, IHubContext<PresenceHub> hub, string me, string id, Scope scope, Action<ResupplyRequest> apply)
    {
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return Results.Forbid();
        string myRegId = ctx.Value.reg.Id;
        var r = await db.ResupplyRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null)
            return Results.NotFound(new ApiError("Demande introuvable."));

        bool allowed = scope switch
        {
            Scope.Own => r.RegimentId == myRegId
                && (r.CreatedBySteamId == me
                    || await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles)),
            Scope.OwnerOrClaimer => r.RegimentId == myRegId || r.ClaimedBySteamId == me,
            _ => false,
        };
        if (!allowed)
            return Results.Forbid();

        string ownerReg = r.RegimentId;
        apply(r);
        await db.SaveChangesAsync();
        await NotifyRegimentAsync(hub, db, ownerReg, PresenceEvents.ResupplyChanged);
        if (myRegId != ownerReg)
            await NotifyRegimentAsync(hub, db, myRegId, PresenceEvents.ResupplyChanged);
        return Results.Ok(await BuildListAsync(db, me));
    }

    private static async Task<bool> CanSeeAsync(AppDbContext db, ResupplyRequest r, string myRegId)
    {
        if (r.RegimentId == myRegId || r.Visibility == ResupplyVisibility.Public)
            return true;
        if (r.Visibility == ResupplyVisibility.Alliance)
            return (await AlliedIdsAsync(db, myRegId)).Contains(r.RegimentId);
        return false;
    }

    private static async Task<List<ResupplyRequestDto>> BuildListAsync(AppDbContext db, string me)
    {
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return new List<ResupplyRequestDto>();
        string regId = ctx.Value.reg.Id;
        bool canManageAll = await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles);
        var allies = await AlliedIdsAsync(db, regId);

        // Visibles : les miennes + alliance (régiments alliés) + publiques (tout le monde).
        var reqs = await db.ResupplyRequests
            .Where(r => r.RegimentId == regId
                || r.Visibility == ResupplyVisibility.Public
                || (r.Visibility == ResupplyVisibility.Alliance && allies.Contains(r.RegimentId)))
            .ToListAsync();

        var reqIds = reqs.Select(r => r.Id).ToList();
        var allItems = await db.ResupplyRequestItems.Where(i => reqIds.Contains(i.RequestId)).ToListAsync();
        var itemsByReq = allItems.GroupBy(i => i.RequestId)
            .ToDictionary(g => g.Key, g => g.Select(i => new ResupplyItemDto(i.Code, i.Name, i.Category, i.Quantity)).ToList());

        var userIds = reqs.SelectMany(r => new[] { r.CreatedBySteamId, r.ClaimedBySteamId })
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.SteamId)).ToDictionaryAsync(u => u.SteamId, u => u.DisplayName);

        var ownerRegIds = reqs.Select(r => r.RegimentId).Distinct().ToList();
        var regNames = await db.Regiments.Where(rg => ownerRegIds.Contains(rg.Id)).ToDictionaryAsync(rg => rg.Id, rg => rg.Name);

        var claimerIds = reqs.Where(r => r.ClaimedBySteamId.Length > 0).Select(r => r.ClaimedBySteamId).Distinct().ToList();
        var claimerRegs = await db.RegimentMembers.Where(m => claimerIds.Contains(m.SteamId))
            .ToDictionaryAsync(m => m.SteamId, m => m.RegimentId);

        return reqs.Select(r =>
        {
            bool isMine = r.RegimentId == regId;
            bool claimedByMine = r.ClaimedBySteamId.Length > 0 && claimerRegs.GetValueOrDefault(r.ClaimedBySteamId, "") == regId;
            return new ResupplyRequestDto(
                r.Id, r.Title, r.Hex, r.Coords,
                itemsByReq.GetValueOrDefault(r.Id, new List<ResupplyItemDto>()),
                r.Priority, r.Status, r.Note, r.Visibility,
                regNames.GetValueOrDefault(r.RegimentId, "?"), isMine, claimedByMine,
                r.CreatedBySteamId, names.GetValueOrDefault(r.CreatedBySteamId, "?"),
                r.ClaimedBySteamId, names.GetValueOrDefault(r.ClaimedBySteamId, ""),
                isMine && (canManageAll || r.CreatedBySteamId == me), r.ClaimedBySteamId == me);
        })
        .OrderBy(d => d.Status == ResupplyStatus.Done ? 1 : 0)
        .ThenByDescending(d => d.Priority)
        .ThenBy(d => d.Title)
        .ToList();
    }
}
