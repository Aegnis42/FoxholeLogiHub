using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record ResupplyPriorityOption(int Value, string Label);

/// <summary>Un item dans une demande ou un brouillon (icône + nom + quantité).</summary>
public sealed class ResupplyItemLineViewModel : ObservableObject
{
    public ResupplyItemLineViewModel(string code, string name, string category, int quantity)
    {
        Code = code; Name = name; Category = category; Quantity = quantity;
        Icon = ItemIcons.Load(code);
    }
    public ResupplyItemLineViewModel(ResupplyItemDto d) : this(d.Code, d.Name, d.Category, d.Quantity) { }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public int Quantity { get; }
    public ImageSource? Icon { get; }
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string QtyText => $"×{Quantity:N0}";
}

/// <summary>Une étape de craft affichée dans le plan d'une demande prise en charge.</summary>
public sealed class CraftStepViewModel
{
    public CraftStepViewModel(CraftStep s)
    {
        Code = s.Code; Name = s.Name; Building = s.Building; Units = s.Units; Crates = s.Crates;
        Icon = ItemIcons.Load(s.Code);
    }
    public string Code { get; }
    public string Name { get; }
    public string Building { get; }
    public long Units { get; }
    public int Crates { get; }
    public ImageSource? Icon { get; }
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string BuildingText => $"🏭 {Building}";
    public string QtyText => Crates > 0 ? $"{Units:N0}  ({Crates:N0} caisses)" : $"{Units:N0}";
}

/// <summary>Une ressource brute à récolter (plan).</summary>
public sealed class HarvestLineViewModel
{
    public HarvestLineViewModel(HarvestLine h)
    {
        Code = h.Code; Name = h.Name; Quantity = h.Quantity;
        Icon = ItemIcons.Load(h.Code);
    }
    public string Code { get; }
    public string Name { get; }
    public long Quantity { get; }
    public ImageSource? Icon { get; }
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string QtyText => $"{Quantity:N0}";
}

/// <summary>Un manque détecté (item sous son seuil) → à ajouter au brouillon d'une demande.</summary>
public sealed class ResupplyNeedViewModel : ObservableObject
{
    public ResupplyNeedViewModel(StockpileAlertDto a)
    {
        Code = a.Code; Name = a.Name; Category = a.Category;
        StockpileName = a.StockpileName; Hex = a.Hex; Town = a.Town;
        Quantity = a.Quantity;
        int target = a.LowThreshold > 0 ? a.LowThreshold : a.CriticalThreshold;
        Deficit = Math.Max(0, target - a.Quantity);
        IsCritical = a.Severity == "critical";
        Icon = ItemIcons.Load(Code);
    }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public string StockpileName { get; }
    public string Hex { get; }
    public string Town { get; }
    public int Quantity { get; }
    public int Deficit { get; }
    public bool IsCritical { get; }
    public ImageSource? Icon { get; }

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string DeficitText => $"manque {Deficit:N0}";
    public string LocationLabel
    {
        get { string loc = string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}"; return $"{StockpileName} — {loc}"; }
    }
    public Brush DeficitBrush => IsCritical
        ? new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x3A))
        : new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x2E));
}

/// <summary>Une demande de ravitaillement (nom, lieu, items) + son plan de production.</summary>
public sealed class ResupplyRequestViewModel : ObservableObject
{
    private readonly Lazy<ProductionPlan> _plan;

    public ResupplyRequestViewModel(ResupplyRequestDto d)
    {
        Id = d.Id;
        Title = d.Title;
        Hex = d.Hex;
        Coords = d.Coords;
        Priority = d.Priority;
        Status = d.Status;
        Note = d.Note;
        CreatedByName = d.CreatedByName;
        ClaimedByName = d.ClaimedByName;
        CanManage = d.CanManage;
        MineClaim = d.MineClaim;
        foreach (var it in d.Items)
            Items.Add(new ResupplyItemLineViewModel(it));

        var pairs = d.Items.Select(i => (i.Code, i.Quantity)).ToList();
        _plan = new Lazy<ProductionPlan>(() => ProductionPlanner.Compute(pairs));
    }

    public string Id { get; }
    public string Title { get; }
    public string Hex { get; }
    public string Coords { get; }
    public int Priority { get; }
    public string Status { get; }
    public string Note { get; }
    public string CreatedByName { get; }
    public string ClaimedByName { get; }
    public bool CanManage { get; }
    public bool MineClaim { get; }
    public System.Collections.ObjectModel.ObservableCollection<ResupplyItemLineViewModel> Items { get; } = new();

    public bool IsDone => Status == ResupplyStatus.Done;
    public bool IsOpen => !IsDone;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public bool HasClaim => !string.IsNullOrWhiteSpace(ClaimedByName);

    public string LocationLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Hex)) parts.Add(Hex);
            if (!string.IsNullOrWhiteSpace(Coords)) parts.Add(Coords);
            return parts.Count > 0 ? string.Join(" · ", parts) : "Lieu non précisé";
        }
    }

    public string PriorityLabel => Priority switch
    { ResupplyPriority.Urgent => "Urgente", ResupplyPriority.High => "Haute", _ => "Normale" };
    public bool ShowPriority => Priority > 0;
    public Brush PriorityBrush => Priority switch
    {
        ResupplyPriority.Urgent => new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x3A)),
        ResupplyPriority.High => new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x2E)),
        _ => new SolidColorBrush(Color.FromRgb(0x3A, 0x41, 0x4C)),
    };

    public string StatusLabel => Status switch
    { ResupplyStatus.Done => "Livrée", ResupplyStatus.Claimed => "Pris en charge", _ => "Ouverte" };
    public Brush StatusBrush => Status switch
    {
        ResupplyStatus.Done => new SolidColorBrush(Color.FromRgb(0x3A, 0x8A, 0x4F)),
        ResupplyStatus.Claimed => new SolidColorBrush(Color.FromRgb(0x2A, 0x6E, 0x8F)),
        _ => new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x2A)),
    };

    public string CreatedInfo => $"par {CreatedByName}";
    public string ClaimInfo => HasClaim ? $"Pris par {ClaimedByName}" : "";
    public string ClaimButtonText => MineClaim ? "✓ Je m'en occupe" : (HasClaim ? "Reprendre" : "Prendre en charge");
    public Brush ClaimButtonBrush => MineClaim
        ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x43))
        : new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x45));

    // --- Plan de production (calculé à la demande) ---
    public IReadOnlyList<CraftStepViewModel> Crafts => _plan.Value.Crafts.Select(c => new CraftStepViewModel(c)).ToList();
    public IReadOnlyList<HarvestLineViewModel> Harvests => _plan.Value.Harvests.Select(h => new HarvestLineViewModel(h)).ToList();
    public bool HasCrafts => _plan.Value.HasCrafts;
    public bool HasHarvests => _plan.Value.HasHarvests;
    public string VehiclesText => $"≈ {_plan.Value.Vehicles} véhicule(s)  ·  {_plan.Value.FinalCrates:N0} caisses à livrer (~15/camion)";
}
