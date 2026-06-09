using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record CatalogItem(string Code, string Name, string Category);

/// <summary>
/// Catalogue d'items pour la saisie manuelle. Chargé depuis <c>Data/fir-catalog.json</c> (mêmes
/// codes que l'import FIR) : chaque item a donc une icône et reste cohérent avec l'auto-import.
/// La saisie libre reste possible pour ce qui n'est pas au catalogue.
/// </summary>
public static class FoxholeItemCatalog
{
    public static readonly IReadOnlyList<CatalogItem> Items = Load();

    public static readonly IReadOnlyList<string> Names =
        Items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    private static readonly Dictionary<string, CatalogItem> ByName =
        Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, CatalogItem> ByCode =
        Items.GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Résout une saisie (nom du catalogue, code, ou texte libre) en (Code, Nom, Catégorie).</summary>
    public static CatalogItem Resolve(string input)
    {
        string text = (input ?? "").Trim();
        if (ByName.TryGetValue(text, out var byName))
            return byName;
        if (ByCode.TryGetValue(text, out var byCode))
            return byCode;
        string code = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(code))
            code = text;
        return new CatalogItem(code, text, "Autres");
    }

    private static IReadOnlyList<CatalogItem> Load()
    {
        var list = new List<CatalogItem>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "fir-catalog.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                    string name = p.Value.TryGetProperty("name", out var n) ? n.GetString() ?? p.Name : p.Name;
                    string cat = p.Value.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                    list.Add(new CatalogItem(p.Name, name, cat));
                }
            }
        }
        catch
        {
            // Catalogue absent/illisible : sélecteur vide, la saisie libre reste possible.
        }
        return list;
    }
}
