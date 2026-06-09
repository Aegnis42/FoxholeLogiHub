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
                _map[p.Name] = (name, cat);
            }
        }
        catch
        {
            // Catalogue absent/illisible : on retombera sur les codenames bruts.
        }
    }

    public (string Name, string Category) Resolve(string code) =>
        _map.TryGetValue(code, out var v) ? v : (code, "Importé");
}
