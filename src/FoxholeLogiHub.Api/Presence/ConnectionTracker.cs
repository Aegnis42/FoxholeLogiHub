using System.Collections.Concurrent;

namespace FoxholeLogiHub.Api.Presence;

/// <summary>
/// Suit en mémoire les utilisateurs connectés (par Steam ID) et leur nombre de connexions
/// SignalR actives. Un utilisateur peut être connecté depuis plusieurs fenêtres.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly ConcurrentDictionary<string, int> _connections = new();

    /// <summary>Ajoute une connexion. Retourne true si l'utilisateur vient de passer en ligne.</summary>
    public bool Add(string steamId)
    {
        bool becameOnline = false;
        _connections.AddOrUpdate(steamId,
            _ => { becameOnline = true; return 1; },
            (_, count) => count + 1);
        return becameOnline;
    }

    /// <summary>Retire une connexion. Retourne true si l'utilisateur vient de passer hors ligne.</summary>
    public bool Remove(string steamId)
    {
        bool wentOffline = false;
        _connections.AddOrUpdate(steamId,
            _ => 0,
            (_, count) => count - 1);

        if (_connections.TryGetValue(steamId, out int remaining) && remaining <= 0)
        {
            if (_connections.TryRemove(steamId, out _))
                wentOffline = true;
        }
        return wentOffline;
    }

    public bool IsOnline(string steamId) =>
        _connections.TryGetValue(steamId, out int count) && count > 0;
}
