using System.IO;
using System.Text.Json;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Table de correspondance codename → (nom affichable, catégorie), générée depuis le catalogue
/// FIR (Data/fir-catalog.json). Sert à afficher de vrais noms après un import par capture.
/// </summary>
public sealed class FirCatalog
{
    private readonly Dictionary<string, (string Name, string Category)> _map = new(StringComparer.OrdinalIgnoreCase);

    public FirCatalog()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "fir-catalog.json");
            if (!File.Exists(path))
                return;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                string name = p.Value.TryGetProperty("name", out var n) ? n.GetString() ?? p.Name : p.Name;
                string cat = p.Value.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                string disp = p.Value.TryGetProperty("display", out var d) ? d.GetString() ?? "" : "";
                _map[p.Name] = (MakeLabel(name, disp), cat);
            }
        }
        catch
        {
            // Catalogue absent/illisible : on retombera sur les codenames bruts.
        }
    }

    // Munitions : préfixe par le calibre (« 40mm — Cannon Ammo ») pour rester reconnaissable comme en jeu.
    private static string MakeLabel(string name, string display)
    {
        bool isCaliber = display.Length > 0 && char.IsDigit(display[0])
            && display.Contains("mm", StringComparison.OrdinalIgnoreCase);
        return isCaliber && !display.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? $"{display} — {name}"
            : name;
    }

    public (string Name, string Category) Resolve(string code) =>
        _map.TryGetValue(code, out var v) ? v : (code, "Importé");
}
