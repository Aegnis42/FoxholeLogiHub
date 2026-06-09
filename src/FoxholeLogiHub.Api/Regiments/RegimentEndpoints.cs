using System.Security.Claims;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Regiments;

public static class RegimentEndpoints
{
    private const int AllPermissions = (int)(RegimentPermission.ManageMembers | RegimentPermission.ManageRoles
        | RegimentPermission.Invite | RegimentPermission.ManageRegiment | RegimentPermission.ManageAlliances);

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
                Name = req.Name.Trim(),
                Tag = (req.Tag ?? "").Trim(),
                Faction = string.IsNullOrWhiteSpace(req.Faction) ? "Unknown" : req.Faction,
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
            await NotifyRegimentAsync(hub, db, reg.Id);

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
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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

            ctx.Value.reg.Name = req.Name.Trim();
            ctx.Value.reg.Tag = (req.Tag ?? "").Trim();
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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

        // --- Rôles ---

        app.MapPost("/api/regiments/roles", async (CreateRoleRequest req, ClaimsPrincipal p, AppDbContext db, ConnectionTracker tracker, IHubContext<PresenceHub> hub) =>
        {
            string me = Me(p);
            var ctx = await MyRegimentAsync(db, me);
            if (ctx is null || !await HasPermAsync(db, ctx.Value.reg, ctx.Value.member, me, RegimentPermission.ManageRoles))
                return Results.Forbid();
            db.RegimentRoles.Add(new RegimentRole { RegimentId = ctx.Value.reg.Id, Name = req.Name.Trim(), Permissions = req.Permissions });
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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
            role.Name = req.Name.Trim();
            role.Permissions = req.Permissions;
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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
            foreach (var m in db.RegimentMembers.Where(m => m.RegimentId == ctx.Value.reg.Id && m.RoleId == role.Id))
                m.RoleId = defaultRoleId;
            db.RegimentRoles.Remove(role);
            await db.SaveChangesAsync();
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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
            await NotifyRegimentAsync(hub, db, ctx.Value.reg.Id);
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
                await NotifyRegimentAsync(hub, db, reg.Id);
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
            await NotifyRegimentAsync(hub, db, target.Id);
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
            await NotifyRegimentAsync(hub, db, mine);
            await NotifyRegimentAsync(hub, db, other);
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
            await NotifyRegimentAsync(hub, db, mine);
            await NotifyRegimentAsync(hub, db, other);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // ---------- Helpers ----------

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

    private static async Task NotifyRegimentAsync(IHubContext<PresenceHub> hub, AppDbContext db, string regimentId)
    {
        var memberIds = await db.RegimentMembers.Where(m => m.RegimentId == regimentId).Select(m => m.SteamId).ToListAsync();
        if (memberIds.Count > 0)
            await hub.Clients.Users(memberIds).SendAsync(PresenceEvents.RegimentChanged);
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
