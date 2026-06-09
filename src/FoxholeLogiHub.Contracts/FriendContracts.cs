namespace FoxholeLogiHub.Contracts;

// NB : l'identité de l'appelant (Steam ID) vient désormais du JWT, plus du corps de requête.

/// <summary>Données pour créer/mettre à jour son propre profil côté serveur.</summary>
public sealed record UpsertUserRequest(string DisplayName, string Faction);

/// <summary>Représentation publique d'un utilisateur (avec son code d'ami).</summary>
public sealed record UserDto(string SteamId, string DisplayName, string Faction, string FriendCode);

/// <summary>Envoi d'une demande d'ami par code.</summary>
public sealed record SendFriendRequestRequest(string FriendCode);

/// <summary>
/// Résultat d'un envoi de demande. <see cref="Accepted"/> est vrai si la demande a été
/// auto-acceptée (cas où la cible nous avait déjà envoyé une demande).
/// </summary>
public sealed record SendFriendRequestResult(bool Accepted, string TargetSteamId, string DisplayName);

/// <summary>Réponse à une demande d'ami reçue (accepter / refuser).</summary>
public sealed record RespondFriendRequestRequest(string RequesterSteamId, bool Accept);

/// <summary>Demande de suppression d'ami.</summary>
public sealed record RemoveFriendRequest(string FriendSteamId);

/// <summary>Une demande d'ami reçue (en attente).</summary>
public sealed record FriendRequestDto(string FromSteamId, string DisplayName, string Faction, bool HasAvatar);

/// <summary>Un ami dans la liste, avec son statut de présence et la présence d'un avatar.</summary>
public sealed record FriendDto(string SteamId, string DisplayName, string Faction, bool Online, bool HasAvatar);

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

    /// <summary>Serveur → client : une nouvelle demande d'ami est arrivée (recharger les demandes).</summary>
    public const string FriendRequestReceived = "FriendRequestReceived";

    /// <summary>Serveur → client : la liste d'amis a changé (acceptation/suppression) — recharger.</summary>
    public const string FriendsChanged = "FriendsChanged";

    /// <summary>Serveur → client : une invitation de régiment est arrivée — recharger les invitations.</summary>
    public const string RegimentInviteReceived = "RegimentInviteReceived";

    /// <summary>Serveur → client : le régiment a changé (membre, rôle, alliance…) — recharger.</summary>
    public const string RegimentChanged = "RegimentChanged";
}
