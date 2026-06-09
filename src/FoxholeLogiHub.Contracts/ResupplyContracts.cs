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

/// <summary>Crée une demande de ravitaillement (item + quantité, stockpile cible optionnel).</summary>
public sealed record CreateResupplyRequest(string Code, string Name, string Category, int Quantity, string StockpileId, int Priority, string Note);

/// <summary>Action sur une demande existante (prise en charge / fait / réouverture / suppression).</summary>
public sealed record ResupplyActionRequest(string Id);

/// <summary>
/// Une demande de ravitaillement visible par le régiment. <see cref="CanManage"/> = je peux la
/// supprimer (créateur ou droit ManageStockpiles) ; <see cref="MineClaim"/> = c'est moi qui l'ai prise.
/// </summary>
public sealed record ResupplyRequestDto(
    string Id, string Code, string Name, string Category, int Quantity,
    string StockpileId, string StockpileName, string Hex, string Town,
    int Priority, string Status, string Note,
    string CreatedBy, string CreatedByName,
    string ClaimedBy, string ClaimedByName,
    bool CanManage, bool MineClaim);
