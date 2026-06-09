using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record CatalogItem(string Code, string Name, string Category);
public sealed record ItemIngredient(string Code, int Qty);

/// <summary>Fiche complète d'un item (catégorie corrigée, caisse, recette de craft).</summary>
public sealed class ItemEntry
{
    public required string Code { get; init; }
    public required string Name { get; init; }       // ChassisName (nom propre)
    public required string Display { get; init; }     // DisplayName (calibre pour les munitions)
    public required string Category { get; init; }    // catégorie FR corrigée
    public required string Label { get; init; }       // libellé sélecteur (calibre préfixé)
    public int CrateSize { get; init; }               // unités par caisse (0 = non caissable)
    public int ProdTime { get; init; }                // temps de production d'une caisse (s)
    public required IReadOnlyList<ItemIngredient> Ingredients { get; init; }
    public required IReadOnlyList<string> Buildings { get; init; } // Factory / MassProductionFactory

    public bool HasRecipe => Ingredients.Count > 0;
    public bool IsCratable => CrateSize > 0;
}

/// <summary>
/// Base d'items Foxhole chargée depuis <c>Data/items.json</c> (générée du catalogue jeu FIR) :
/// catégories corrigées, taille de caisse, recettes de craft. Sert au sélecteur, à l'affichage,
/// à la résolution d'icône (<see cref="AliasToCode"/>) et à la fiche détaillée (<see cref="Get"/>).
/// </summary>
public static class FoxholeItemCatalog
{
    private static readonly IReadOnlyList<ItemEntry> _entries = Load();

    private static readonly Dictionary<string, ItemEntry> _byCode =
        _entries.GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<CatalogItem> Items =
        _entries.Select(e => new CatalogItem(e.Code, e.Label, e.Category)).ToList();

    public static readonly IReadOnlyList<string> Names =
        Items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    private static readonly Dictionary<string, CatalogItem> _byLabel =
        Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> _aliasToCode = BuildAliases();

    /// <summary>Fiche d'un item par code (avec repli alias : « 40mm » → LightTankAmmo). null si inconnu.</summary>
    public static ItemEntry? Get(string code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        if (_byCode.TryGetValue(code, out var e))
            return e;
        string? alias = AliasToCode(code);
        return alias is not null && _byCode.TryGetValue(alias, out var e2) ? e2 : null;
    }

    /// <summary>Catégorie corrigée d'un item par code (via alias). null si inconnu.</summary>
    public static string? CategoryOf(string code) => Get(code)?.Category;

    /// <summary>Résout une saisie (libellé, nom, calibre, code) en (Code FIR, Nom affiché, Catégorie).</summary>
    public static CatalogItem Resolve(string input)
    {
        string text = (input ?? "").Trim();
        if (_byLabel.TryGetValue(text, out var byLabel))
            return byLabel;

        string? code = AliasToCode(text);
        if (code is not null && _byCode.TryGetValue(code, out var e))
            return new CatalogItem(e.Code, e.Label, e.Category);

        string free = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (free.Length == 0)
            free = text;
        return new CatalogItem(free, text, "Autres");
    }

    /// <summary>Retrouve le CodeName FIR (donc l'icône/catégorie) depuis un code/nom/calibre stocké. null si inconnu.</summary>
    public static string? AliasToCode(string codeOrName)
    {
        string key = Norm(codeOrName);
        return key.Length > 0 && _aliasToCode.TryGetValue(key, out var code) ? code : null;
    }

    private static string Norm(string s) =>
        new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>();
        void Add(string s, string code) { string k = Norm(s); if (k.Length > 0 && !map.ContainsKey(k)) map[k] = code; }
        foreach (var e in _entries) Add(e.Code, e.Code);
        foreach (var e in _entries)
        {
            Add(e.Display, e.Code);
            Add(e.Name, e.Code);
            Add(e.Label, e.Code);
        }
        return map;
    }

    private static string MakeLabel(string name, string display)
    {
        bool isCaliber = display.Length > 0 && char.IsDigit(display[0])
            && display.Contains("mm", StringComparison.OrdinalIgnoreCase);
        return isCaliber && !display.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? $"{display} — {name}"
            : name;
    }

    private static IReadOnlyList<ItemEntry> Load()
    {
        var list = new List<ItemEntry>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "items.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                  try
                  {
                    var v = p.Value;
                    string name = Str(v, "name", p.Name);
                    string disp = Str(v, "display", "");
                    var ingredients = new List<ItemIngredient>();
                    if (v.TryGetProperty("ingredients", out var ing) && ing.ValueKind == JsonValueKind.Array)
                        foreach (var it in ing.EnumerateArray())
                            ingredients.Add(new ItemIngredient(Str(it, "code", ""), Int(it, "qty")));
                    var buildings = new List<string>();
                    if (v.TryGetProperty("buildings", out var b) && b.ValueKind == JsonValueKind.Array)
                        foreach (var it in b.EnumerateArray())
                            buildings.Add(it.GetString() ?? "");

                    list.Add(new ItemEntry
                    {
                        Code = p.Name,
                        Name = name,
                        Display = disp,
                        Category = Str(v, "category", "Autres"),
                        Label = MakeLabel(name, disp),
                        CrateSize = Int(v, "crateSize"),
                        ProdTime = Int(v, "prodTime"),
                        Ingredients = ingredients,
                        Buildings = buildings,
                    });
                  }
                  catch
                  {
                    // Entrée malformée : on l'ignore, le reste du catalogue reste chargé.
                  }
                }
            }
        }
        catch
        {
            // Base absente/illisible : sélecteur vide, saisie libre toujours possible.
        }
        return list;
    }

    private static string Str(JsonElement e, string prop, string fallback) =>
        e.TryGetProperty(prop, out var v) ? v.GetString() ?? fallback : fallback;

    private static int Int(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.Number)
            return 0;
        // Tolère les nombres non entiers (ex. prodTime 37.5) — GetInt32() lèverait sinon.
        return v.TryGetInt32(out int i) ? i : (int)Math.Round(v.GetDouble());
    }
}
