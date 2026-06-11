using FoxholeLogiHub.Api.Common;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static FoxholeLogiHub.Api.Common.RegimentGuards;
using ClaimsPrincipal = System.Security.Claims.ClaimsPrincipal;

namespace FoxholeLogiHub.Api.Regiments;

public static class RegimentEndpoints
{
    private const int AllPermissions = (int)(RegimentPermission.ManageMembers | RegimentPermission.ManageRoles
        | RegimentPermission.Invite | RegimentPermission.ManageRegiment | RegimentPermission.ManageAlliances
        | RegimentPermission.ManageStockpiles | RegimentPermission.LogiGranular);

    public static void MapRegimentEndpoints(this WebApplication app)
    {
        // --- Création / consultation ---

        app.MapPost("/api/regiments", async (CreateRegimentRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker) =>
        {
            string me = Me(p);
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new ApiError("Nom du régiment requis."));
            if (await db.RegimentMembers.AnyAsync(m => m.SteamId == me))
                return Results.BadRequest(new ApiError("Tu es déjà dans un régiment."));

            var reg = new Regiment
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Validate.Str(req.Name, 64),
                Tag = Validate.Str(req.Tag, 8),
                Faction = string.IsNullOrWhiteSpace(req.Faction) ? "Unknown" : Validate.Str(req.Faction, 32),
                InviteCode = await UniqueCodeAsync(db),
                OwnerSteamId = me,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Regiments.Add(reg);

            var chef = new RegimentRole { RegimentId = reg.Id, Name = "Chef", Permissions = AllPermissions };
            var officier = new RegimentRole { RegimentId = reg.Id, Name = "Officier", Permissions = (int)(RegimentPermission.ManageMembers | RegimentPermission.Invite | RegimentPermission.ManageAlliances) };
            var membre = new RegimentRole { RegimentId = reg.Id, Name = "Membre", Permissions = 0, IsDefault = true };
            db.RegimentRoles.AddRange(chef, officier, membre);
            await db.SaveChangesAsync();

            db.RegimentMembers.Add(new RegimentMember { RegimentId = reg.Id, SteamId = me, RoleId = chef.Id, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            return Results.Ok(await BuildDtoAsync(db, tracker, reg, me));
        }).RequireAuthorization();

        app.MapGet("/api/regiments/mine", async (ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            return ctx is null ? Results.Ok((RegimentDto?)null) : Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        app.MapPost("/api/regiments/join", async (JoinRegimentRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            if (await db.RegimentMembers.AnyAsync(m => m.SteamId == me))
                return Results.BadRequest(new ApiError("Tu es déjà dans un régiment (quitte-le d'abord)."));

            string code = FriendCodeGenerator.Normalize(req.InviteCode);
            var reg = await db.Regiments.FirstOrDefaultAsync(r => r.InviteCode == code);
            if (reg is null)
                return Results.NotFound(new ApiError("Aucun régiment avec ce code."));

            int defaultRoleId = await db.RegimentRoles.Where(r => r.RegimentId == reg.Id && r.IsDefault).Select(r => r.Id).FirstAsync();
            db.RegimentMembers.Add(new RegimentMember { RegimentId = reg.Id, SteamId = me, RoleId = defaultRoleId, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, reg.Id, PresenceEvents.RegimentChanged);

            return Results.Ok(await BuildDtoAsync(db, tracker, reg, me));
        }).RequireAuthorization();

        app.MapPost("/api/regiments/leave", async (ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null)
                return Results.BadRequest(new ApiError("Tu n'es dans aucun régiment."));
            if (ctx.Value.reg.OwnerSteamId == me)
                return Results.BadRequest(new ApiError("Le chef ne peut pas quitter : transfère ou supprime le régiment."));

            db.RegimentMembers.Remove(ctx.Value.member);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapDelete("/api/regiments", async (ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || ctx.Value.reg.OwnerSteamId != me)
                return Results.BadRequest(new ApiError("Seul le chef peut supprimer le régiment."));

            string regId = ctx.Value.reg.Id;
            var memberIds = await db.RegimentMembers.Where(m => m.RegimentId == regId).Select(m => m.SteamId).ToListAsync();

            db.RegimentMembers.RemoveRange(db.RegimentMembers.Where(m => m.RegimentId == regId));
            db.RegimentRoles.RemoveRange(db.RegimentRoles.Where(r => r.RegimentId == regId));
            db.RegimentInvites.RemoveRange(db.RegimentInvites.Where(i => i.RegimentId == regId));
            db.RegimentAlliances.RemoveRange(db.RegimentAlliances.Where(a => a.RegimentAId == regId || a.RegimentBId == regId));
            db.Regiments.Remove(ctx.Value.reg);
            await db.SaveChangesAsync();

            if (memberIds.Count > 0)
                await hub.Clients.Users(memberIds).SendAsync(PresenceEvents.RegimentChanged);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPut("/api/regiments", async (UpdateRegimentRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageRegiment))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new ApiError("Nom requis."));

            ctx.Value.reg.Name = Validate.Str(req.Name, 64);
            ctx.Value.reg.Tag = Validate.Str(req.Tag, 8);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        app.MapPost("/api/regiments/regenerate-code", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.Invite))
                return Results.Forbid();
            ctx.Value.reg.InviteCode = await UniqueCodeAsync(db);
            await db.SaveChangesAsync();
            return Results.Ok(new { inviteCode = ctx.Value.reg.InviteCode });
        }).RequireAuthorization();

        // Webhook Discord du régiment (chef uniquement) : "" = désactivé.
        app.MapPost("/api/regiments/webhook", async (SetRegimentWebhookRequest req, ClaimsPrincipal p, AppDbContext db, DiscordNotifier discord) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || ctx.Value.reg.OwnerSteamId != me)
                return Results.BadRequest(new ApiError("Seul le chef peut configurer le webhook Discord."));
            string url = Validate.Str(req.Url ?? "", 256).Trim();

            // L'URL est masquée côté client : champ URL vide + mention fournie + webhook déjà
            // configuré = on ne modifie QUE la mention. (Désactiver = URL vide ET mention null.)
            bool tagOnly = url.Length == 0 && req.RoleTag is not null && ctx.Value.reg.DiscordWebhookUrl.Length > 0;
            if (tagOnly)
                url = ctx.Value.reg.DiscordWebhookUrl;
            if (url.Length > 0 && !DiscordNotifier.LooksValid(url))
                return Results.BadRequest(new ApiError("URL invalide — colle l'URL du webhook Discord (https://discord.com/api/webhooks/…)."));

            // Mention optionnelle (ID de rôle, <@&id>, @everyone/@here) — null = inchangée.
            string roleTag = ctx.Value.reg.DiscordRoleTag;
            if (req.RoleTag is not null)
            {
                string? normalized = DiscordNotifier.NormalizeRoleTag(req.RoleTag);
                if (normalized is null)
                    return Results.BadRequest(new ApiError("Mention invalide — colle l'ID du rôle Discord (clic droit sur le rôle → Copier l'identifiant), ou @everyone / @here."));
                roleTag = normalized;
            }
            if (url.Length == 0)
                roleTag = ""; // webhook désactivé → mention effacée aussi

            ctx.Value.reg.DiscordWebhookUrl = url;
            ctx.Value.reg.DiscordRoleTag = roleTag;
            await db.SaveChangesAsync();
            if (url.Length > 0)
                discord.Send(url, DiscordNotifier.Tagged(roleTag, tagOnly
                    ? $"🔔 Mention configurée pour **{DiscordNotifier.Safe(ctx.Value.reg.Name)}** — les prochaines alertes pingueront ce rôle."
                    : $"✅ Webhook FoxholeLogiHub connecté pour **{DiscordNotifier.Safe(ctx.Value.reg.Name)}** [{DiscordNotifier.Safe(ctx.Value.reg.Tag)}] — les alertes de stock, de ravitaillement et de menace arriveront ici."));
            return Results.Ok(BuildWebhookDto(url, roleTag));
        }).RequireAuthorization();

        app.MapGet("/api/regiments/webhook", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || ctx.Value.reg.OwnerSteamId != me)
                return Results.BadRequest(new ApiError("Seul le chef peut voir le webhook Discord."));
            return Results.Ok(BuildWebhookDto(ctx.Value.reg.DiscordWebhookUrl, ctx.Value.reg.DiscordRoleTag));
        }).RequireAuthorization();

        // Fin de guerre : purge les données logistiques du régiment (stockpiles + contenus + partages
        // + demandes de ravitaillement) pour repartir propre. Réservé au CHEF — le client propose
        // une archive locale avant d'appeler.
        app.MapPost("/api/regiments/war-reset", async (ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || ctx.Value.reg.OwnerSteamId != me)
                return Results.BadRequest(new ApiError("Seul le chef peut lancer le reset de fin de guerre."));

            string regId = ctx.Value.reg.Id;
            var spIds = await db.Stockpiles.Where(s => s.RegimentId == regId).Select(s => s.Id).ToListAsync();
            int items = await db.StockpileItems.Where(i => spIds.Contains(i.StockpileId)).ExecuteDeleteAsync();
            await db.StockpileShares.Where(sh => spIds.Contains(sh.StockpileId)).ExecuteDeleteAsync();
            int stockpiles = await db.Stockpiles.Where(s => s.RegimentId == regId).ExecuteDeleteAsync();

            var reqIds = await db.ResupplyRequests.Where(r => r.RegimentId == regId).Select(r => r.Id).ToListAsync();
            await db.ResupplyRequestItems.Where(i => reqIds.Contains(i.RequestId)).ExecuteDeleteAsync();
            int requests = await db.ResupplyRequests.Where(r => r.RegimentId == regId).ExecuteDeleteAsync();

            await NotifyRegimentAsync(hub, db, regId, PresenceEvents.StockpilesChanged);
            await NotifyRegimentAsync(hub, db, regId, PresenceEvents.ResupplyChanged);
            return Results.Ok(new WarResetResultDto(stockpiles, items, requests));
        }).RequireAuthorization();

        // --- Rôles ---

        app.MapPost("/api/regiments/roles", async (CreateRoleRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageRoles))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new ApiError("Nom du rôle requis."));
            db.RegimentRoles.Add(new RegimentRole { RegimentId = ctx.Value.reg.Id, Name = Validate.Str(req.Name, 48), Permissions = req.Permissions });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        app.MapPut("/api/regiments/roles", async (UpdateRoleRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageRoles))
                return Results.Forbid();
            var role = await db.RegimentRoles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.RegimentId == ctx.Value.reg.Id);
            if (role is null)
                return Results.NotFound(new ApiError("Rôle introuvable."));
            role.Name = Validate.Str(req.Name, 48);
            role.Permissions = req.Permissions;
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        app.MapPost("/api/regiments/roles/delete", async (UpdateRoleRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageRoles))
                return Results.Forbid();
            var role = await db.RegimentRoles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.RegimentId == ctx.Value.reg.Id);
            if (role is null)
                return Results.NotFound(new ApiError("Rôle introuvable."));
            if (role.IsDefault)
                return Results.BadRequest(new ApiError("Le rôle par défaut ne peut pas être supprimé."));

            int defaultRoleId = await db.RegimentRoles.Where(r => r.RegimentId == ctx.Value.reg.Id && r.IsDefault).Select(r => r.Id).FirstAsync();
            var affected = await db.RegimentMembers.Where(m => m.RegimentId == ctx.Value.reg.Id && m.RoleId == role.Id).ToListAsync();
            foreach (var m in affected)
                m.RoleId = defaultRoleId;
            db.RegimentRoles.Remove(role);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        // --- Membres ---

        app.MapPost("/api/regiments/members/role", async (SetMemberRoleRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageMembers))
                return Results.Forbid();
            if (req.MemberSteamId == ctx.Value.reg.OwnerSteamId)
                return Results.BadRequest(new ApiError("On ne change pas le rôle du chef."));
            var target = await db.RegimentMembers.FirstOrDefaultAsync(m => m.RegimentId == ctx.Value.reg.Id && m.SteamId == req.MemberSteamId);
            var role = await db.RegimentRoles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.RegimentId == ctx.Value.reg.Id);
            if (target is null || role is null)
                return Results.NotFound(new ApiError("Membre ou rôle introuvable."));
            target.RoleId = role.Id;
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        app.MapPost("/api/regiments/members/kick", async (KickMemberRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageMembers))
                return Results.Forbid();
            if (req.MemberSteamId == ctx.Value.reg.OwnerSteamId)
                return Results.BadRequest(new ApiError("Le chef ne peut pas être exclu."));
            var target = await db.RegimentMembers.FirstOrDefaultAsync(m => m.RegimentId == ctx.Value.reg.Id && m.SteamId == req.MemberSteamId);
            if (target is null)
                return Results.NotFound(new ApiError("Membre introuvable."));
            db.RegimentMembers.Remove(target);
            await db.SaveChangesAsync();
            await hub.Clients.User(req.MemberSteamId).SendAsync(PresenceEvents.RegimentChanged); // l'exclu rafraîchit
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id, PresenceEvents.RegimentChanged);
            return Results.Ok(await BuildDtoAsync(db, tracker, ctx.Value.reg, me));
        }).RequireAuthorization();

        // --- Invitations d'amis ---

        app.MapPost("/api/regiments/invite", async (InviteFriendToRegimentRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.Invite))
                return Results.Forbid();

            bool isFriend = await db.Friendships.AnyAsync(f => f.UserSteamId == me && f.FriendSteamId == req.FriendSteamId);
            if (!isFriend)
                return Results.BadRequest(new ApiError("Tu ne peux inviter que tes amis."));
            if (await db.RegimentMembers.AnyAsync(m => m.SteamId == req.FriendSteamId))
                return Results.BadRequest(new ApiError("Ce joueur est déjà dans un régiment."));

            bool exists = await db.RegimentInvites.AnyAsync(i => i.RegimentId == ctx.Value.reg.Id && i.ToSteamId == req.FriendSteamId);
            if (!exists)
            {
                db.RegimentInvites.Add(new RegimentInvite { RegimentId = ctx.Value.reg.Id, ToSteamId = req.FriendSteamId, FromSteamId = me, CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            await hub.Clients.User(req.FriendSteamId).SendAsync(PresenceEvents.RegimentInviteReceived);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/regiments/invites", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            string me = Me(p);
            var list = await (
                from i in db.RegimentInvites
                join r in db.Regiments on i.RegimentId equals r.Id
                join u in db.Users on i.FromSteamId equals u.SteamId
                where i.ToSteamId == me
                orderby i.Id
                select new RegimentInviteDto(r.Id, r.Name, r.Tag, r.Faction, u.DisplayName)
            ).ToListAsync();
            return Results.Ok(list);
        }).RequireAuthorization();

        app.MapPost("/api/regiments/invites/respond", async (RespondRegimentInviteRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var invite = await db.RegimentInvites.FirstOrDefaultAsync(i => i.RegimentId == req.RegimentId && i.ToSteamId == me);
            if (invite is null)
                return Results.NotFound(new ApiError("Invitation introuvable."));
            db.RegimentInvites.Remove(invite);

            if (req.Accept)
            {
                if (await db.RegimentMembers.AnyAsync(m => m.SteamId == me))
                {
                    await db.SaveChangesAsync();
                    return Results.BadRequest(new ApiError("Tu es déjà dans un régiment."));
                }
                var reg = await db.Regiments.FirstOrDefaultAsync(r => r.Id == req.RegimentId);
                if (reg is null)
                {
                    await db.SaveChangesAsync();
                    return Results.NotFound(new ApiError("Régiment introuvable."));
                }
                int defaultRoleId = await db.RegimentRoles.Where(r => r.RegimentId == reg.Id && r.IsDefault).Select(r => r.Id).FirstAsync();
                db.RegimentMembers.Add(new RegimentMember { RegimentId = reg.Id, SteamId = me, RoleId = defaultRoleId, JoinedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
                await NotifyRegimentAsync(hub, db, reg.Id, PresenceEvents.RegimentChanged);
            }
            else
            {
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }).RequireAuthorization();

        // --- Alliances ---

        app.MapPost("/api/regiments/alliances/propose", async (ProposeAllianceRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageAlliances))
                return Results.Forbid();

            string code = FriendCodeGenerator.Normalize(req.TargetInviteCode);
            var target = await db.Regiments.FirstOrDefaultAsync(r => r.InviteCode == code);
            if (target is null)
                return Results.NotFound(new ApiError("Aucun régiment avec ce code."));
            if (target.Id == ctx.Value.reg.Id)
                return Results.BadRequest(new ApiError("Un régiment ne peut pas s'allier avec lui-même."));

            string a = ctx.Value.reg.Id, b = target.Id;
            bool exists = await db.RegimentAlliances.AnyAsync(x =>
                (x.RegimentAId == a && x.RegimentBId == b) || (x.RegimentAId == b && x.RegimentBId == a));
            if (exists)
                return Results.BadRequest(new ApiError("Une alliance (ou demande) existe déjà avec ce régiment."));

            db.RegimentAlliances.Add(new RegimentAlliance { RegimentAId = a, RegimentBId = b, ProposedByRegimentId = a, Accepted = false, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, target.Id, PresenceEvents.RegimentChanged);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/regiments/alliances/respond", async (RespondAllianceRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageAlliances))
                return Results.Forbid();

            string mine = ctx.Value.reg.Id, other = req.OtherRegimentId;
            var alliance = await db.RegimentAlliances.FirstOrDefaultAsync(x =>
                ((x.RegimentAId == mine && x.RegimentBId == other) || (x.RegimentAId == other && x.RegimentBId == mine))
                && !x.Accepted && x.ProposedByRegimentId == other);
            if (alliance is null)
                return Results.NotFound(new ApiError("Demande d'alliance introuvable."));

            if (req.Accept)
                alliance.Accepted = true;
            else
                db.RegimentAlliances.Remove(alliance);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, mine, PresenceEvents.RegimentChanged);
            await NotifyRegimentAsync(hub, db, other, PresenceEvents.RegimentChanged);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/regiments/alliances/remove", async (RemoveAllianceRequest req, ClaimsPrincipal p, AppDbContext db, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageAlliances))
                return Results.Forbid();
            string mine = ctx.Value.reg.Id, other = req.OtherRegimentId;
            var rows = db.RegimentAlliances.Where(x =>
                (x.RegimentAId == mine && x.RegimentBId == other) || (x.RegimentAId == other && x.RegimentBId == mine));
            db.RegimentAlliances.RemoveRange(rows);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, mine, PresenceEvents.RegimentChanged);
            await NotifyRegimentAsync(hub, db, other, PresenceEvents.RegimentChanged);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // ---------- Helpers (spécifiques aux régiments — le reste vient de RegimentGuards) ----------

    /// <summary>URL masquée pour l'affichage (jamais le secret complet).</summary>
    private static RegimentWebhookDto BuildWebhookDto(string url, string roleTag) =>
        new(url.Length > 0, url.Length > 45 ? url[..42] + "•••" : url, roleTag);

    private static async Task<string> UniqueCodeAsync(AppDbContext db)
    {
        for (int i = 0; i < 20; i++)
        {
            string code = FriendCodeGenerator.Generate();
            if (!await db.Regiments.AnyAsync(r => r.InviteCode == code))
                return code;
        }
        throw new InvalidOperationException("Impossible de générer un code de régiment unique.");
    }

    private static async Task<RegimentDto> BuildDtoAsync(AppDbContext db, ConnectionTracker tracker, Regiment reg, string meSteamId)
    {
        var roles = await db.RegimentRoles.Where(r => r.RegimentId == reg.Id).ToListAsync();
        var roleById = roles.ToDictionary(r => r.Id);

        var memberRows = await (
            from m in db.RegimentMembers
            join u in db.Users on m.SteamId equals u.SteamId
            where m.RegimentId == reg.Id
            select new { m.SteamId, u.DisplayName, u.Faction, HasAvatar = u.AvatarPng != null, m.RoleId }
        ).ToListAsync();

        var members = memberRows
            .Select(x => new RegimentMemberDto(
                x.SteamId, x.DisplayName, x.Faction, tracker.IsOnline(x.SteamId), x.HasAvatar,
                x.RoleId, roleById.TryGetValue(x.RoleId, out var r) ? r.Name : "?",
                x.SteamId == reg.OwnerSteamId))
            .OrderByDescending(m => m.IsOwner)
            .ThenByDescending(m => m.Online)
            .ThenBy(m => m.DisplayName)
            .ToList();

        var roleDtos = roles.Select(r => new RegimentRoleDto(r.Id, r.Name, r.Permissions, r.IsDefault)).ToList();

        var allianceRows = await db.RegimentAlliances
            .Where(a => a.RegimentAId == reg.Id || a.RegimentBId == reg.Id).ToListAsync();
        var alliances = new List<RegimentAllianceDto>();
        foreach (var a in allianceRows)
        {
            string otherId = a.RegimentAId == reg.Id ? a.RegimentBId : a.RegimentAId;
            var other = await db.Regiments.FirstOrDefaultAsync(r => r.Id == otherId);
            if (other is null)
                continue;
            alliances.Add(new RegimentAllianceDto(other.Id, other.Name, other.Tag, other.Faction,
                a.Accepted ? "accepted" : "pending", a.ProposedByRegimentId == reg.Id));
        }

        bool iAmOwner = reg.OwnerSteamId == meSteamId;
        int myPerms;
        if (iAmOwner)
        {
            myPerms = AllPermissions;
        }
        else
        {
            var meMember = memberRows.FirstOrDefault(x => x.SteamId == meSteamId);
            myPerms = meMember is not null && roleById.TryGetValue(meMember.RoleId, out var mr) ? mr.Permissions : 0;
        }

        return new RegimentDto(reg.Id, reg.Name, reg.Tag, reg.Faction, reg.InviteCode,
            reg.OwnerSteamId, iAmOwner, myPerms, members, roleDtos, alliances);
    }
}
