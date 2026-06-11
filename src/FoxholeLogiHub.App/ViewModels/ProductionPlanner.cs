using System;
using System.Collections.Generic;
using System.Linq;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Une étape de craft (quoi produire, combien, où).</summary>
public sealed record CraftStep(string Code, string Name, string Building, long Units, int Crates, int Depth);

/// <summary>Une ressource à récolter (brute, en bas de la chaîne).</summary>
public sealed record HarvestLine(string Code, string Name, long Quantity);

/// <summary>Plan de production complet d'une demande (crafts en chaîne, récoltes, véhicules).</summary>
public sealed class ProductionPlan
{
    public IReadOnlyList<CraftStep> Crafts { get; init; } = new List<CraftStep>();
    public IReadOnlyList<HarvestLine> Harvests { get; init; } = new List<HarvestLine>();
    public int FinalCrates { get; init; }
    public int Vehicles { get; init; }
    public bool HasCrafts => Crafts.Count > 0;
    public bool HasHarvests => Harvests.Count > 0;
}

/// <summary>
/// Traduit un plan de production en iconTypes de structures de la carte (où produire, où récolter).
/// Les bâtiments de facility (Bétonnière, Métallurgie…) n'ont pas d'icône publique → ignorés.
/// </summary>
public static class ProductionMapIcons
{
    public static HashSet<int> For(ProductionPlan plan)
    {
        var icons = new HashSet<int>();
        foreach (var c in plan.Crafts)
        {
            switch (c.Building)
            {
                case "Usine": icons.Add(34); break;
                case "MPF": icons.Add(51); break;
                case "Raffinerie": icons.Add(17); break;
            }
        }
        foreach (var h in plan.Harvests)
        {
            switch (h.Code)
            {
                case "Metal": icons.Add(20); icons.Add(38); break;        // Salvage
                case "Components": icons.Add(21); icons.Add(40); break;
                case "Sulfur": icons.Add(23); icons.Add(32); break;
                case "Coal": icons.Add(61); break;
                case "Oil": icons.Add(62); icons.Add(75); break;
                case "Petrol": icons.Add(22); break;
            }
        }
        return icons;
    }
}

/// <summary>
/// Décompose une liste d'items demandés en chaîne de production complète (jusqu'aux ressources
/// à récolter), à partir des recettes de <see cref="FoxholeItemCatalog"/>. Estime aussi le nombre
/// de véhicules pour livrer les items finis.
/// </summary>
public static class ProductionPlanner
{
    private const int CratesPerVehicle = 15; // ~ un camion / flatbed

    public static ProductionPlan Compute(IEnumerable<(string code, int qty)> items)
    {
        var crafts = new Dictionary<string, (string building, long units, int depth)>(StringComparer.OrdinalIgnoreCase);
        var harvests = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string code, long qty, int depth)>();

        int finalCrates = 0;
        foreach (var (code, qty) in items)
        {
            if (qty <= 0) continue;
            queue.Enqueue((code, qty, 0));
            var e = FoxholeItemCatalog.Get(code);
            int cs = e?.CrateSize ?? 0;
            finalCrates += cs > 0 ? (int)Math.Ceiling(qty / (double)cs) : qty;
        }

        long guard = 0;
        while (queue.Count > 0 && guard++ < 2_000_000)
        {
            var (code, qty, depth) = queue.Dequeue();
            if (depth > 12) { harvests[code] = harvests.GetValueOrDefault(code) + qty; continue; }

            var e = FoxholeItemCatalog.Get(code);
            var recipe = e?.Recipes.FirstOrDefault(r => r.Ingredients.Count > 0);
            if (e is null || recipe is null)
            {
                harvests[code] = harvests.GetValueOrDefault(code) + qty;
                continue;
            }

            string building = recipe.Buildings.FirstOrDefault() ?? "?";
            if (crafts.TryGetValue(code, out var cur))
                crafts[code] = (building, cur.units + qty, Math.Min(cur.depth, depth));
            else
                crafts[code] = (building, qty, depth);

            int output = Math.Max(1, recipe.Output);
            long batches = (long)Math.Ceiling(qty / (double)output);
            foreach (var ing in recipe.Ingredients)
                queue.Enqueue((ing.Code, batches * ing.Qty, depth + 1));
        }

        var craftSteps = crafts.Select(kv =>
        {
            var e = FoxholeItemCatalog.Get(kv.Key);
            int cs = e?.CrateSize ?? 0;
            int crates = cs > 0 ? (int)Math.Ceiling(kv.Value.units / (double)cs) : 0;
            return new CraftStep(kv.Key, e?.Name ?? kv.Key, kv.Value.building, kv.Value.units, crates, kv.Value.depth);
        }).OrderBy(c => c.Depth).ThenBy(c => c.Name).ToList();

        var harvestLines = harvests.Select(kv =>
        {
            var e = FoxholeItemCatalog.Get(kv.Key);
            return new HarvestLine(kv.Key, e?.Name ?? kv.Key, kv.Value);
        }).OrderByDescending(h => h.Quantity).ToList();

        int vehicles = finalCrates > 0 ? Math.Max(1, (int)Math.Ceiling(finalCrates / (double)CratesPerVehicle)) : 0;
        return new ProductionPlan { Crafts = craftSteps, Harvests = harvestLines, FinalCrates = finalCrates, Vehicles = vehicles };
    }
}
