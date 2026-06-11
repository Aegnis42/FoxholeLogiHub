namespace FoxholeLogiHub.Contracts;

/// <summary>Permissions au sein d'un régiment (combinables). Le chef (propriétaire) les a toutes.</summary>
[Flags]
public enum RegimentPermission
{
    None = 0,
    ManageMembers = 1,    // changer le rôle des membres, exclure
    ManageRoles = 2,      // créer/modifier/supprimer des rôles
    Invite = 4,           // inviter des amis, voir/régénérer le code
    ManageRegiment = 8,    // éditer le régiment, le supprimer
    ManageAlliances = 16,  // proposer/accepter/rompre des alliances

    /// <summary>Parapluie « admin logistique » : couvre TOUTES les permissions granulaires ci-dessous.</summary>
    ManageStockpiles = 32,

    // Permissions logistiques granulaires (chacune est aussi accordée par ManageStockpiles).
    StockpileCreate = 64,    // créer des stockpiles du régiment (privés)
    StockpileShare = 128,    // créer des stockpiles publics / partager-départager à l'alliance
    StockpileEdit = 256,     // modifier le contenu : import F8, items, seuils, templates, position, transferts
    StockpileDelete = 512,   // supprimer un stockpile
    MpfManage = 1024,        // récupérer/supprimer les commandes MPF des autres membres
    // Créer une demande POUR LE RÉGIMENT (visibilité privée) est ouvert à tous les membres.
    ResupplyShare = 2048,    // publier une demande au-delà du régiment : visibilité alliance ou publique
    ResupplyManage = 4096,   // gérer les demandes du régiment : livrer/rouvrir/supprimer/visibilité/réattribuer

    /// <summary>Masque des permissions couvertes par le parapluie ManageStockpiles.</summary>
    LogiGranular = StockpileCreate | StockpileShare | StockpileEdit | StockpileDelete | MpfManage
        | ResupplyShare | ResupplyManage,
}

public sealed record CreateRegimentRequest(string Name, string Tag, string Faction);
public sealed record JoinRegimentRequest(string InviteCode);
public sealed record UpdateRegimentRequest(string Name, string Tag);

public sealed record RegimentRoleDto(int Id, string Name, int Permissions, bool IsDefault);

public sealed record RegimentMemberDto(
    string SteamId, string DisplayName, string Faction, bool Online, bool HasAvatar,
    int RoleId, string RoleName, bool IsOwner);

public sealed record RegimentAllianceDto(
    string RegimentId, string Name, string Tag, string Faction, string Status, bool ProposedByUs);

/// <summary>Vue complète du régiment de l'utilisateur.</summary>
public sealed record RegimentDto(
    string Id, string Name, string Tag, string Faction, string InviteCode,
    string OwnerSteamId, bool IAmOwner, int MyPermissions,
    List<RegimentMemberDto> Members, List<RegimentRoleDto> Roles, List<RegimentAllianceDto> Alliances);

/// <summary>Invitation de régiment reçue (en attente).</summary>
public sealed record RegimentInviteDto(string RegimentId, string Name, string Tag, string Faction, string FromDisplayName);

public sealed record InviteFriendToRegimentRequest(string FriendSteamId);
public sealed record RespondRegimentInviteRequest(string RegimentId, bool Accept);

public sealed record CreateRoleRequest(string Name, int Permissions);
public sealed record UpdateRoleRequest(int RoleId, string Name, int Permissions);
public sealed record SetMemberRoleRequest(string MemberSteamId, int RoleId);
public sealed record KickMemberRequest(string MemberSteamId);

public sealed record ProposeAllianceRequest(string TargetInviteCode);
public sealed record RespondAllianceRequest(string OtherRegimentId, bool Accept);
public sealed record RemoveAllianceRequest(string OtherRegimentId);

/// <summary>Configure le webhook Discord du régiment ("" = désactiver) + mention à préfixer
/// aux notifications (ID de rôle, &lt;@&amp;id&gt;, @everyone/@here ; "" = aucune). Chef uniquement.</summary>
public sealed record SetRegimentWebhookRequest(string Url, string? RoleTag = null);

/// <summary>État du webhook Discord (URL masquée, jamais le secret complet) + mention configurée.</summary>
public sealed record RegimentWebhookDto(bool Configured, string Masked, string RoleTag = "");
