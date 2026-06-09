using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.Presence;

/// <summary>Identifie l'utilisateur SignalR par le Steam ID du JWT vérifié.</summary>
public sealed class SteamIdUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(TokenService.SteamIdClaim)?.Value;
}

/// <summary>
/// Hub de présence : à la connexion, marque l'utilisateur en ligne et prévient ses amis ;
/// à la déconnexion, le marque hors ligne et prévient ses amis. Envoie aussi au client
/// connectant la liste de ses amis déjà en ligne.
/// </summary>
[Authorize]
public sealed class PresenceHub : Hub
{
    private readonly ConnectionTracker _tracker;
    private readonly AppDbContext _db;

    public PresenceHub(ConnectionTracker tracker, AppDbContext db)
    {
        _tracker = tracker;
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        string? steamId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(steamId))
        {
            await base.OnConnectedAsync();
            return;
        }

        bool becameOnline = _tracker.Add(steamId);

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
        if (user is not null)
        {
            user.LastSeenAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }

        List<string> friendIds = await _db.Friendships
            .Where(f => f.UserSteamId == steamId)
            .Select(f => f.FriendSteamId)
            .ToListAsync();

        // Liste des amis déjà en ligne → envoyée au client qui se connecte.
        List<string> onlineFriends = friendIds.Where(_tracker.IsOnline).ToList();
        await Clients.Caller.SendAsync(PresenceEvents.OnlineFriends, onlineFriends);

        // Prévient les amis en ligne que je viens d'arriver.
        if (becameOnline && friendIds.Count > 0)
            await Clients.Users(friendIds).SendAsync(PresenceEvents.PresenceChanged, steamId, true);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? steamId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(steamId))
        {
            bool wentOffline = _tracker.Remove(steamId);
            if (wentOffline)
            {
                List<string> friendIds = await _db.Friendships
                    .Where(f => f.UserSteamId == steamId)
                    .Select(f => f.FriendSteamId)
                    .ToListAsync();

                if (friendIds.Count > 0)
                    await Clients.Users(friendIds).SendAsync(PresenceEvents.PresenceChanged, steamId, false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
