using System.Security.Claims;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Common;

/// <summary>Garde-fous d'entrée : tronque les chaînes et borne les quantités (anti-abus).</summary>
public static class Validate
{
    public const int MaxQuantity = 10_000_000;
    public const int MaxImportItems = 2_000;
    public const int MaxRequestItems = 100;

    /// <summary>Trim + tronque à <paramref name="max"/> caractères — on ne stocke jamais de texte arbitrairement long.</summary>
    public static string Str(string? s, int max)
    {
        string t = (s ?? "").Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>Borne une quantité dans [0, MaxQuantity].</summary>
    public static int Qty(int value) => Math.Clamp(value, 0, MaxQuantity);
}

/// <summary>
/// Identité (Steam ID du jeton), appartenance au régiment, permissions et notifications —
/// partagés par tous les groupes d'endpoints (régiments, stockpiles, ravitaillement).
/// </summary>
public static class RegimentGuards
{
    public static string Me(ClaimsPrincipal p) =>
        p.FindFirstValue(TokenService.SteamIdClaim) ?? throw new InvalidOperationException("Jeton sans Steam ID.");

    public static async Task<(Regiment reg, RegimentMember member)?> MyRegimentAsync(AppDbContext db, string steamId)
    {
        var member = await db.RegimentMembers.FirstOrDefaultAsync(m => m.SteamId == steamId);
        if (member is null)
            return null;
        var reg = await db.Regiments.FirstOrDefaultAsync(r => r.Id == member.RegimentId);
        return reg is null ? null : (reg, member);
    }

    public static async Task<bool> HasPermAsync(AppDbContext db, Regiment reg, RegimentMember member, string steamId, RegimentPermission perm)
    {
        if (reg.OwnerSteamId == steamId)
            return true;
        var role = await db.RegimentRoles.FirstOrDefaultAsync(r => r.Id == member.RoleId);
        return role is not null && ((RegimentPermission)role.Permissions & perm) == perm;
    }

    /// <summary>Ids des régiments en alliance acceptée avec <paramref name="regId"/>.</summary>
    public static async Task<List<string>> AlliedIdsAsync(AppDbContext db, string regId)
    {
        var rows = await db.RegimentAlliances
            .Where(a => a.Accepted && (a.RegimentAId == regId || a.RegimentBId == regId)).ToListAsync();
        return rows.Select(a => a.RegimentAId == regId ? a.RegimentBId : a.RegimentAId).Distinct().ToList();
    }

    /// <summary>Pousse un événement temps réel à tous les membres d'un régiment.</summary>
    public static async Task NotifyRegimentAsync(IHubContext<PresenceHub> hub, AppDbContext db, string regimentId, string eventName)
    {
        var memberIds = await db.RegimentMembers.Where(m => m.RegimentId == regimentId).Select(m => m.SteamId).ToListAsync();
        if (memberIds.Count > 0)
            await hub.Clients.Users(memberIds).SendAsync(eventName);
    }
}
