namespace FoxholeLogiHub.Contracts;

/// <summary>Types de structures portant un stockpile. Le code n'a de sens que pour Seaport/StorageDepot.</summary>
public static class StockpileTypes
{
    public const string Factory = "Factory";
    public const string Refinery = "Refinery";
    public const string MassProductionFactory = "MassProductionFactory";
    public const string ProductionBase = "ProductionBase";
    public const string Seaport = "Seaport";
    public const string StorageDepot = "StorageDepot";
    public const string Bunker = "Bunker";

    public static readonly string[] All =
    {
        StorageDepot, Seaport, Factory, MassProductionFactory, Refinery, ProductionBase, Bunker,
    };

    /// <summary>Un code (mot de passe) ne s'applique qu'aux Port et Dépôt.</summary>
    public static bool UsesCode(string type) => type is Seaport or StorageDepot;
}

/// <summary><see cref="MapX"/>/<see cref="MapY"/> : position 0..1 dans la boîte englobante de
/// l'hexagone (posée depuis la carte) — null si le stockpile n'a pas été placé sur la carte.</summary>
public sealed record CreateStockpileRequest(string Name, string Hex, string Town, string Type, string Code, bool IsPublic,
    double? MapX = null, double? MapY = null);
public sealed record UpdateStockpileRequest(string Id, string Name, string Hex, string Town, string Type, string Code, bool IsPublic);
public sealed record DeleteStockpileRequest(string Id);

/// <summary>Repositionne un stockpile sur la carte (hexagone + position 0..1 + ville recalculée).</summary>
public sealed record SetStockpilePositionRequest(string Id, string Hex, string Town, double MapX, double MapY);
public sealed record ShareStockpileRequest(string StockpileId, string RegimentId);
public sealed record UnshareStockpileRequest(string StockpileId, string RegimentId);

/// <summary>
/// Un stockpile visible par l'utilisateur. <see cref="IsOwn"/> = appartient à mon régiment ;
/// <see cref="CanManage"/> = je peux l'éditer ; <see cref="SharedRegimentIds"/> = régiments alliés
/// avec qui il est partagé (rempli seulement pour mes stockpiles).
/// </summary>
/// <summary>
/// <see cref="TownControl"/> : contrôle de la ville selon l'API War (valeurs de <see cref="WarTownControl"/>),
/// relatif à la faction du régiment de l'appelant ; <see cref="TownScorched"/> = ville rasée.
/// </summary>
public sealed record StockpileDto(
    string Id, string RegimentId, string RegimentName, string Name, string Hex, string Town,
    string Type, string Code, bool IsPublic, bool IsOwn, bool CanManage, List<string> SharedRegimentIds,
    string TownControl, bool TownScorched, double? MapX, double? MapY);

/// <summary>Un item d'un stockpile (quantité + seuils d'alerte). Name/Category dénormalisés.</summary>
public sealed record StockpileItemDto(string Code, string Name, string Category, int Quantity, int LowThreshold, int CriticalThreshold);

/// <summary>Template d'objectifs de seuils, partagé au régiment.</summary>
public sealed record StockpileTemplateDto(string Id, string Name, int ItemCount);
/// <summary>Crée (ou remplace, à nom égal) un template à partir des seuils d'un stockpile.</summary>
public sealed record CreateTemplateFromStockpileRequest(string StockpileId, string Name);
/// <summary>Applique les seuils d'un template à un stockpile (les items absents sont créés à 0).</summary>
public sealed record ApplyTemplateRequest(string TemplateId, string StockpileId);
public sealed record DeleteTemplateRequest(string TemplateId);

/// <summary>Un point d'historique (quantité d'un item à l'instant d'un import).</summary>
public sealed record HistoryPointDto(DateTimeOffset At, int Quantity);

/// <summary>Historique 30 jours d'un item d'un stockpile (un point par import).</summary>
public sealed record StockpileItemHistoryDto(string Code, List<HistoryPointDto> Points);

/// <summary>Résultat de la recherche globale d'items (tous les stockpiles visibles).</summary>
public sealed record StockpileItemSearchResultDto(
    string StockpileId, string StockpileName, string Hex, string Town, string Type,
    string RegimentName, bool IsOwn,
    string Code, string Name, string Category, int Quantity);

/// <summary>Définit un item dans un stockpile (upsert ; quantité ≤ 0 = retrait).</summary>
public sealed record SetStockpileItemRequest(string StockpileId, string Code, string Name, string Category, int Quantity, int LowThreshold, int CriticalThreshold);

/// <summary>Remplace tout le contenu d'un stockpile (utilisé par l'import auto/capture).</summary>
public sealed record ImportStockpileItemsRequest(string StockpileId, List<StockpileItemDto> Items);

/// <summary>
/// Une alerte de stock (item sous son seuil) pour le tableau de bord, avec le contexte du stockpile.
/// <see cref="Severity"/> = "critical" (qty ≤ seuil critique) ou "low" (qty ≤ seuil bas).
/// </summary>
public sealed record StockpileAlertDto(
    string StockpileId, string StockpileName, string RegimentName, bool IsOwn,
    string Hex, string Town, string Type,
    string Code, string Name, string Category,
    int Quantity, int LowThreshold, int CriticalThreshold, string Severity);
