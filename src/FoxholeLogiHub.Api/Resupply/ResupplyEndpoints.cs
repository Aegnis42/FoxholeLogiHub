using System.Security.Claims;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Resupply;

public static class ResupplyEndpoints
{
    public static void MapResupplyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/resupply", async (ClaimsPrincipal p, AppDbContext db) =>
            Results.Ok(await BuildListAsync(db, Me(p)))).RequireAuthorization();

        app.MapPost("/api/resupply", async (CreateResupplyRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.BadRequest(new ApiError("Rejoins un régiment d'abord."));
            string code = (req.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code) || req.Quantity <= 0)
                return Results.BadRequest(new ApiError("Item et quantité requis."));

            string spId = "";
            if (!string.IsNullOrWhiteSpace(req.StockpileId))
            {
                var sp = await db.Stockpiles.FirstOrDefaultAsync(s => s.Id == req.StockpileId && s.RegimentId == ctx.Value.reg.Id);
                if (sp is not null)
                    spId = sp.Id;
            }

            db.ResupplyRequests.Add(new ResupplyRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                RegimentId = ctx.Value.reg.Id,
                Code = code,
                Name = string.IsNullOrWhiteSpace(req.Name) ? code : req.Name.Trim(),
                Category = (req.Category ?? "").Trim(),
                Quantity = req.Quantity,
                StockpileId = spId,
                Priority = Math.Clamp(req.Priority, 0, 2),
                Status = ResupplyStatus.Open,
                Note = (req.Note ?? "").Trim(),
                CreatedBySteamId = me,
                ClaimedBySteamId = "",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await NotifyAsync(hub, db, ctx.Value.reg.Id);
            return Results.Ok(await BuildListAsync(db, me));
        }).RequireAuthorization();

        // Prendre en charge / se désengager (bascule).
        app.MapPost("/api/resupply/claim", async (ResupplyActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, false, r =>
            {
                if (r.ClaimedBySteamId == Me(p))
                { r.ClaimedBySteamId = ""; if (r.Status == ResupplyStatus.Claimed) r.Status = ResupplyStatus.Open; }
                else
                { r.ClaimedBySteamId = Me(p); if (r.Status == ResupplyStatus.Open) r.Status = ResupplyStatus.Claimed; }
            })).RequireAuthorization();

        // Marquer livrée.
        app.MapPost("/api/resupply/done", async (ResupplyActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, false, r => r.Status = ResupplyStatus.Done)).RequireAuthorization();

        // Rouvrir.
        app.MapPost("/api/resupply/reopen", async (ResupplyActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, false, r =>
            { r.Status = r.ClaimedBySteamId.Length > 0 ? ResupplyStatus.Claimed : ResupplyStatus.Open; })).RequireAuthorization();

        // Supprimer (créateur ou droit ManageStockpiles).
        app.MapPost("/api/resupply/delete", async (ResupplyActionRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
            await MutateAsync(db, hub, Me(p), req.Id, true, r => db.ResupplyRequests.Remove(r))).RequireAuthorization();
    }

    private static async Task<IResult> MutateAsync(AppDbContext db, IHubContext<PresenceHub> hub, string me, string id, bool needManage, Action<ResupplyRequest> apply)
    {
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return Results.Forbid();
        var r = await db.ResupplyRequests.FirstOrDefaultAsync(x => x.Id == id && x.RegimentId == ctx.Value.reg.Id);
        if (r is null)
            return Results.NotFound(new ApiError("Demande introuvable."));
        if (needManage && r.CreatedBySteamId != me
            && !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles))
            return Results.Forbid();

        apply(r);
        await db.SaveChangesAsync();
        await NotifyAsync(hub, db, ctx.Value.reg.Id);
        return Results.Ok(await BuildListAsync(db, me));
    }

    private static async Task<List<ResupplyRequestDto>> BuildListAsync(AppDbContext db, string me)
    {
        var ctx = await MyRegimentAsync(db, me);
        if (ctx is null)
            return new List<ResupplyRequestDto>();
        string regId = ctx.Value.reg.Id;
        bool canManageAll = await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageStockpiles);

        var reqs = await db.ResupplyRequests.Where(r => r.RegimentId == regId).ToListAsync();
        var ids = reqs.SelectMany(r => new[] { r.CreatedBySteamId, r.ClaimedBySteamId })
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var names = await db.Users.Where(u => ids.Contains(u.SteamId)).ToDictionaryAsync(u => u.SteamId, u => u.DisplayName);
        var spIds = reqs.Select(r => r.StockpileId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var sps = await db.Stockpiles.Where(s => spIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);

        return reqs.Select(r =>
        {
            sps.TryGetValue(r.StockpileId, out var sp);
            return new ResupplyRequestDto(
                r.Id, r.Code, r.Name, r.Category, r.Quantity,
                r.StockpileId, sp?.Name ?? "", sp?.Hex ?? "", sp?.Town ?? "",
                r.Priority, r.Status, r.Note,
                r.CreatedBySteamId, names.GetValueOrDefault(r.CreatedBySteamId, "?"),
                r.ClaimedBySteamId, names.GetValueOrDefault(r.ClaimedBySteamId, ""),
                canManageAll || r.CreatedBySteamId == me, r.ClaimedBySteamId == me);
        })
        .OrderBy(d => d.Status == ResupplyStatus.Done ? 1 : 0)
        .ThenByDescending(d => d.Priority)
        .ThenBy(d => d.Name)
        .ToList();
    }

    private static async Task NotifyAsync(IHubContext<PresenceHub> hub, AppDbContext db, string regimentId)
    {
        var memberIds = await db.RegimentMembers.Where(m => m.RegimentId == regimentId).Select(m => m.SteamId).ToListAsync();
        if (memberIds.Count > 0)
            await hub.Clients.Users(memberIds).SendAsync(PresenceEvents.ResupplyChanged);
    }

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
