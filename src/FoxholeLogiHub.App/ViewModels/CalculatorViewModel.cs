using System.Collections.ObjectModel;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Calculatrice logistique : compose une liste d'items et obtient le plan de production complet
/// (crafts en chaîne, récoltes, véhicules) sans créer de demande de ravitaillement.
/// </summary>
public sealed class CalculatorViewModel : ObservableObject
{
    private string _itemName = "";
    private string _itemQuantity = "";
    private string _status = "Ajoute des items pour calculer le plan de production.";

    public ObservableCollection<ResupplyItemLineViewModel> Items { get; } = new();
    public ObservableCollection<CraftStepViewModel> Crafts { get; } = new();
    public ObservableCollection<HarvestLineViewModel> Harvests { get; } = new();

    public IReadOnlyList<string> ItemNames => FoxholeItemCatalog.Names;

    public string ItemName { get => _itemName; set => Set(ref _itemName, value); }
    public string ItemQuantity { get => _itemQuantity; set => Set(ref _itemQuantity, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool HasItems => Items.Count > 0;
    public bool HasPlan => Crafts.Count > 0 || Harvests.Count > 0;
    public string VehiclesText { get; private set; } = "";

    /// <summary>Envoie la liste vers le brouillon d'une demande de ravitaillement.</summary>
    public event Action<List<(string Code, string Name, string Category, int Qty)>>? SendToResupplyRequested;

    public void AddItem()
    {
        if (string.IsNullOrWhiteSpace(ItemName)) { Status = "Choisis un item."; return; }
        if (!int.TryParse(ItemQuantity.Trim(), out int qty) || qty <= 0) { Status = "Quantité invalide."; return; }

        var cat = FoxholeItemCatalog.Resolve(ItemName);
        var existing = Items.FirstOrDefault(i => i.Code.Equals(cat.Code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Items.Remove(existing);
        Items.Add(new ResupplyItemLineViewModel(cat.Code, cat.Name, cat.Category,
            qty + (existing?.Quantity ?? 0)));

        ItemName = ""; ItemQuantity = "";
        Recompute();
    }

    public void RemoveItem(ResupplyItemLineViewModel item)
    {
        Items.Remove(item);
        Recompute();
    }

    public void Clear()
    {
        Items.Clear();
        Recompute();
    }

    public void SendToResupply()
    {
        if (Items.Count == 0)
            return;
        SendToResupplyRequested?.Invoke(Items
            .Select(i => (i.Code, i.Name, i.Category, i.Quantity)).ToList());
        Status = "Liste envoyée vers le brouillon de demande 🚚";
    }

    private void Recompute()
    {
        Crafts.Clear();
        Harvests.Clear();
        if (Items.Count == 0)
        {
            VehiclesText = "";
            Status = "Ajoute des items pour calculer le plan de production.";
        }
        else
        {
            var plan = ProductionPlanner.Compute(Items.Select(i => (i.Code, i.Quantity)));
            foreach (var c in plan.Crafts)
                Crafts.Add(new CraftStepViewModel(c));
            foreach (var h in plan.Harvests)
                Harvests.Add(new HarvestLineViewModel(h));
            VehiclesText = $"≈ {plan.Vehicles} véhicule(s)  ·  {plan.FinalCrates:N0} caisses à livrer (~15/camion)";
            Status = $"{Items.Count} item(s) — plan calculé.";
        }
        Raise(nameof(HasItems));
        Raise(nameof(HasPlan));
        Raise(nameof(VehiclesText));
    }
}
