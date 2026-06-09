namespace FoxholeLogiHub.Contracts;

/// <summary>États d'une demande de ravitaillement.</summary>
public static class ResupplyStatus
{
    public const string Open = "open";        // ouverte (personne ne s'en occupe)
    public const string Claimed = "claimed";  // prise en charge
    public const string Done = "done";        // livrée
}

/// <summary>Priorités d'une demande (0 = normale, 1 = haute, 2 = urgente).</summary>
public static class ResupplyPriority
{
    public const int Normal = 0;
    public const int High = 1;
    public const int Urgent = 2;
}

/// <summary>Visibilité d'une demande : 0 = privée au régiment, 1 = alliance, 2 = publique (tous).</summary>
public static class ResupplyVisibility
{
    public const int Regiment = 0;
    public const int Alliance = 1;
    public const int Public = 2;
}

/// <summary>Un item demandé dans une demande (plusieurs items par demande).</summary>
public sealed record ResupplyItemDto(string Code, string Name, string Category, int Quantity);

/// <summary>Crée une demande de ravitaillement : nom + localisation + liste d'items + visibilité.</summary>
public sealed record CreateResupplyRequest(string Title, string Hex, string Coords, List<ResupplyItemDto> Items, int Priority, string Note, int Visibility);

/// <summary>Action sur une demande existante (prise en charge / fait / réouverture / suppression).</summary>
public sealed record ResupplyActionRequest(string Id);

/// <summary>Change la visibilité d'une demande (créateur ou droit ManageStockpiles).</summary>
public sealed record SetResupplyVisibilityRequest(string Id, int Visibility);

/// <summary>
/// Une demande de ravitaillement visible par le régiment. <see cref="CanManage"/> = je peux la
/// supprimer (créateur ou droit ManageStockpiles) ; <see cref="MineClaim"/> = c'est moi qui l'ai prise.
/// </summary>
public sealed record ResupplyRequestDto(
    string Id, string Title, string Hex, string Coords,
    List<ResupplyItemDto> Items,
    int Priority, string Status, string Note, int Visibility,
    string OwnerRegimentName, bool IsMine, bool ClaimedByMyRegiment,
    string CreatedBy, string CreatedByName, string ClaimedBy, string ClaimedByName,
    bool CanManage, bool MineClaim);
