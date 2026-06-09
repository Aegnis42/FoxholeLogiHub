using System.Collections.Generic;
using System.Linq;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record CatalogItem(string Code, string Name, string Category);

/// <summary>
/// Catalogue d'items Foxhole pour la saisie manuelle (sélecteur). Liste curée des items
/// logistiques courants ; la saisie libre reste possible pour le reste. L'import auto (FIR)
/// fournira la liste exhaustive plus tard.
/// </summary>
public static class FoxholeItemCatalog
{
    public static readonly IReadOnlyList<CatalogItem> Items = new[]
    {
        // Munitions
        new CatalogItem("7.92mm", "7.92mm", "Munitions"),
        new CatalogItem("7.62mm", "7.62mm", "Munitions"),
        new CatalogItem("9mm", "9mm", "Munitions"),
        new CatalogItem("44", ".44", "Munitions"),
        new CatalogItem("12.7mm", "12.7mm", "Munitions"),
        new CatalogItem("Buckshot", "Buckshot", "Munitions"),
        new CatalogItem("20mm", "20mm", "Munitions"),
        new CatalogItem("30mm", "30mm", "Munitions"),
        new CatalogItem("40mm", "40mm", "Munitions"),
        new CatalogItem("68mm", "68mm (RPG)", "Munitions"),
        new CatalogItem("120mm", "120mm", "Munitions"),
        new CatalogItem("150mm", "150mm", "Munitions"),
        new CatalogItem("250mm", "250mm", "Munitions"),
        new CatalogItem("MortarShell", "Obus de mortier", "Munitions"),

        // Matériaux
        new CatalogItem("BasicMaterials", "Matériaux de base (Bmat)", "Matériaux"),
        new CatalogItem("RefinedMaterials", "Matériaux raffinés (Rmat)", "Matériaux"),
        new CatalogItem("ConstructionMaterials", "Matériaux de construction (Cmat)", "Matériaux"),
        new CatalogItem("ProcessedConstructionMaterials", "Mat. de construction transformés", "Matériaux"),
        new CatalogItem("SteelConstructionMaterials", "Mat. de construction en acier", "Matériaux"),
        new CatalogItem("AssemblyMaterials1", "Matériaux d'assemblage I", "Matériaux"),
        new CatalogItem("AssemblyMaterials2", "Matériaux d'assemblage II", "Matériaux"),
        new CatalogItem("AssemblyMaterials3", "Matériaux d'assemblage III", "Matériaux"),
        new CatalogItem("AssemblyMaterials4", "Matériaux d'assemblage IV", "Matériaux"),
        new CatalogItem("AssemblyMaterials5", "Matériaux d'assemblage V", "Matériaux"),
        new CatalogItem("Sandbag", "Sac de sable", "Matériaux"),
        new CatalogItem("BarbedWire", "Barbelés", "Matériaux"),
        new CatalogItem("MetalBeam", "Poutre métallique", "Matériaux"),

        // Médical
        new CatalogItem("Bandages", "Bandages", "Médical"),
        new CatalogItem("FirstAidKit", "Trousse de premiers soins", "Médical"),
        new CatalogItem("TraumaKit", "Trousse de traumatologie", "Médical"),
        new CatalogItem("BloodPlasma", "Plasma sanguin", "Médical"),
        new CatalogItem("SoldierSupplies", "Ravitaillement soldat", "Médical"),

        // Ressources
        new CatalogItem("Salvage", "Récupération (Salvage)", "Ressources"),
        new CatalogItem("Components", "Composants", "Ressources"),
        new CatalogItem("Sulfur", "Soufre", "Ressources"),
        new CatalogItem("Coke", "Coke", "Ressources"),
        new CatalogItem("Petrol", "Essence", "Ressources"),
        new CatalogItem("HeavyOil", "Huile lourde", "Ressources"),
        new CatalogItem("EnrichedOil", "Huile enrichie", "Ressources"),
        new CatalogItem("Diesel", "Diesel", "Ressources"),
        new CatalogItem("DamagedComponents", "Composants endommagés", "Ressources"),
        new CatalogItem("RareAlloys", "Alliages rares", "Ressources"),
        new CatalogItem("Water", "Eau", "Ressources"),
        new CatalogItem("MaintenanceSupplies", "Fournitures d'entretien", "Ressources"),

        // Explosifs
        new CatalogItem("Grenade", "Grenade", "Explosifs"),
        new CatalogItem("StickyBomb", "Bombe collante", "Explosifs"),
        new CatalogItem("ATGrenade", "Grenade antichar", "Explosifs"),
        new CatalogItem("SmokeGrenade", "Grenade fumigène", "Explosifs"),
        new CatalogItem("ExplosivePowder", "Poudre explosive", "Explosifs"),

        // Ravitaillement / garnison
        new CatalogItem("GarrisonSupplies", "Ravitaillement de garnison", "Ravitaillement"),
        new CatalogItem("Cloth", "Tissu", "Ravitaillement"),
    };

    public static readonly IReadOnlyList<string> Names = Items.Select(i => i.Name).ToList();

    private static readonly Dictionary<string, CatalogItem> ByName =
        Items.ToDictionary(i => i.Name, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Résout une saisie (nom du catalogue ou texte libre) en (Code, Nom, Catégorie).</summary>
    public static CatalogItem Resolve(string input)
    {
        string text = (input ?? "").Trim();
        if (ByName.TryGetValue(text, out var found))
            return found;
        string code = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(code))
            code = text;
        return new CatalogItem(code, text, "Autres");
    }
}
