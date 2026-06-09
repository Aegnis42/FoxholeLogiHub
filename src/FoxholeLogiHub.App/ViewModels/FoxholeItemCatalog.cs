using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record CatalogItem(string Code, string Name, string Category);

/// <summary>
/// Catalogue d'items pour la saisie manuelle. Chargé depuis <c>Data/fir-catalog.json</c> (mêmes
/// <c>CodeName</c> que l'import FIR → icône garantie). Les munitions sont préfixées par leur
/// calibre (« 40mm — Cannon Ammo ») pour être trouvables comme en jeu. <see cref="AliasToCode"/>
/// permet de retrouver le code FIR (et donc l'icône) depuis un calibre/nom déjà stocké.
/// </summary>
public static class FoxholeItemCatalog
{
    private sealed record Entry(string Code, string Name, string Chassis, string Display, string Category, string Label);

    private static readonly IReadOnlyList<Entry> _entries = Load();

    public static readonly IReadOnlyList<CatalogItem> Items =
        _entries.Select(e => new CatalogItem(e.Code, e.Label, e.Category)).ToList();

    public static readonly IReadOnlyList<string> Names =
        Items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

    private static readonly Dictionary<string, CatalogItem> _byLabel =
        Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    // Clé normalisée (minuscule alphanumérique) → CodeName FIR. Sert à retrouver le code/icône
    // depuis un calibre (« 40mm »), un nom (« Basic Materials ») ou un ancien code stocké.
    private static readonly Dictionary<string, string> _aliasToCode = BuildAliases();

    /// <summary>Résout une saisie (libellé, nom, calibre, code) en (Code FIR, Nom affiché, Catégorie).</summary>
    public static CatalogItem Resolve(string input)
    {
        string text = (input ?? "").Trim();
        if (_byLabel.TryGetValue(text, out var byLabel))
            return byLabel;

        string? code = AliasToCode(text);
        if (code is not null)
        {
            var e = _entries.First(x => x.Code == code);
            return new CatalogItem(e.Code, e.Label, e.Category);
        }

        string free = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (free.Length == 0)
            free = text;
        return new CatalogItem(free, text, "Autres");
    }

    /// <summary>
    /// Retrouve le CodeName FIR (donc l'icône) à partir d'un code/nom/calibre déjà stocké
    /// (ex. « 40mm » → « LightTankAmmo », « Basic Materials » → « Cloth »). null si inconnu.
    /// </summary>
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

        // Priorité au code exact, puis calibre / nom / châssis / libellé.
        foreach (var e in _entries) Add(e.Code, e.Code);
        foreach (var e in _entries)
        {
            Add(e.Display, e.Code);
            Add(e.Name, e.Code);
            Add(e.Chassis, e.Code);
            Add(e.Label, e.Code);
        }
        return map;
    }

    private static string MakeLabel(string name, string display)
    {
        // Munitions : préfixe par le calibre (« 40mm — Cannon Ammo ») pour la recherche par calibre.
        bool isCaliber = display.Length > 0
            && char.IsDigit(display[0])
            && display.Contains("mm", StringComparison.OrdinalIgnoreCase);
        return isCaliber && !display.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? $"{display} — {name}"
            : name;
    }

    private static IReadOnlyList<Entry> Load()
    {
        var list = new List<Entry>();
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
                    string disp = p.Value.TryGetProperty("display", out var d) ? d.GetString() ?? "" : "";
                    string chassis = p.Value.TryGetProperty("chassis", out var ch) ? ch.GetString() ?? "" : "";
                    list.Add(new Entry(p.Name, name, chassis, disp, cat, MakeLabel(name, disp)));
                }
            }
        }
        catch
        {
            // Catalogue absent/illisible : sélecteur vide, saisie libre toujours possible.
        }
        return list;
    }
}
