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

    public static readonly string[] All =
    {
        StorageDepot, Seaport, Factory, MassProductionFactory, Refinery, ProductionBase,
    };

    /// <summary>Un code (mot de passe) ne s'applique qu'aux Port et Dépôt.</summary>
    public static bool UsesCode(string type) => type is Seaport or StorageDepot;
}

public sealed record CreateStockpileRequest(string Name, string Hex, string Town, string Type, string Code, bool IsPublic);
public sealed record UpdateStockpileRequest(string Id, string Name, string Hex, string Town, string Type, string Code, bool IsPublic);
public sealed record DeleteStockpileRequest(string Id);
public sealed record ShareStockpileRequest(string StockpileId, string RegimentId);
public sealed record UnshareStockpileRequest(string StockpileId, string RegimentId);

/// <summary>
/// Un stockpile visible par l'utilisateur. <see cref="IsOwn"/> = appartient à mon régiment ;
/// <see cref="CanManage"/> = je peux l'éditer ; <see cref="SharedRegimentIds"/> = régiments alliés
/// avec qui il est partagé (rempli seulement pour mes stockpiles).
/// </summary>
public sealed record StockpileDto(
    string Id, string RegimentId, string RegimentName, string Name, string Hex, string Town,
    string Type, string Code, bool IsPublic, bool IsOwn, bool CanManage, List<string> SharedRegimentIds);

/// <summary>Un item d'un stockpile (quantité). Name/Category sont dénormalisés (indépendant d'un catalogue).</summary>
public sealed record StockpileItemDto(string Code, string Name, string Category, int Quantity);

/// <summary>Définit la quantité d'un item dans un stockpile (upsert ; quantité ≤ 0 = retrait).</summary>
public sealed record SetStockpileItemRequest(string StockpileId, string Code, string Name, string Category, int Quantity);
