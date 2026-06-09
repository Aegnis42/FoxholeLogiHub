namespace FoxholeLogiHub.Contracts;

/// <summary>Données pour créer/mettre à jour l'utilisateur côté serveur (à la connexion de l'app).</summary>
public sealed record UpsertUserRequest(string SteamId, string DisplayName, string Faction);

/// <summary>Représentation publique d'un utilisateur (avec son code d'ami).</summary>
public sealed record UserDto(string SteamId, string DisplayName, string Faction, string FriendCode);

/// <summary>Demande d'ajout d'ami par code.</summary>
public sealed record AddFriendRequest(string SteamId, string FriendCode);

/// <summary>Demande de suppression d'ami.</summary>
public sealed record RemoveFriendRequest(string SteamId, string FriendSteamId);

/// <summary>Un ami dans la liste, avec son statut de présence.</summary>
public sealed record FriendDto(string SteamId, string DisplayName, string Faction, bool Online);

/// <summary>Erreur API renvoyée en JSON.</summary>
public sealed record ApiError(string Message);

/// <summary>
/// Noms des méthodes/événements du hub de présence SignalR (partagés client/serveur
/// pour éviter les chaînes magiques divergentes).
/// </summary>
public static class PresenceEvents
{
    /// <summary>Serveur → client : un ami a changé de statut. Args : (steamId, online).</summary>
    public const string PresenceChanged = "PresenceChanged";

    /// <summary>Serveur → client (à la connexion) : liste des Steam IDs d'amis actuellement en ligne.</summary>
    public const string OnlineFriends = "OnlineFriends";
}
