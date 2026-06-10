using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record ResupplyPriorityOption(int Value, string Label);
public sealed record ResupplyVisibilityOption(int Value, string Label);

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
        ? Palette.Critical
        : Palette.Warning;
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
        Visibility = d.Visibility;
        OwnerRegimentName = d.OwnerRegimentName;
        IsMine = d.IsMine;
        ClaimedByMyRegiment = d.ClaimedByMyRegiment;
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
    public int Visibility { get; }
    public string OwnerRegimentName { get; }
    public bool IsMine { get; }
    public bool ClaimedByMyRegiment { get; }
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
        ResupplyPriority.Urgent => Palette.Critical,
        ResupplyPriority.High => Palette.Warning,
        _ => Palette.Neutral,
    };

    public string StatusLabel => Status switch
    { ResupplyStatus.Done => "Livrée", ResupplyStatus.Claimed => "Pris en charge", _ => "Ouverte" };
    public Brush StatusBrush => Status switch
    {
        ResupplyStatus.Done => Palette.Good,
        ResupplyStatus.Claimed => Palette.BlueInfo,
        _ => Palette.BrownOpen,
    };

    public string CreatedInfo => $"par {CreatedByName}";
    public string ClaimInfo => HasClaim ? $"Pris par {ClaimedByName}" : "";
    public string ClaimButtonText => MineClaim ? "✓ Je m'en occupe" : (HasClaim ? "Reprendre" : "Prendre en charge");
    public Brush ClaimButtonBrush => MineClaim
        ? Palette.GreenDark
        : Palette.Slate;

    // Ce que l'utilisateur courant a le droit de faire (aligné sur les règles serveur) :
    // prendre = libre si personne dessus ; reprendre = soi-même ou le régiment propriétaire ;
    // livrer/rouvrir = régiment propriétaire ou preneur en charge.
    public bool CanClaim => IsOpen && (!HasClaim || MineClaim || IsMine);
    public bool CanComplete => IsOpen && (IsMine || MineClaim);
    public bool CanReopen => IsDone && (IsMine || MineClaim);

    // Visibilité + propriétaire
    public string VisibilityLabel => Visibility switch
    {
        ResupplyVisibility.Public => "🌍 Public",
        ResupplyVisibility.Alliance => "🤝 Alliance",
        _ => "🔒 Privé",
    };
    public Brush VisibilityBrush => Visibility switch
    {
        ResupplyVisibility.Public => Palette.VisPublic,
        ResupplyVisibility.Alliance => Palette.VisAlliance,
        _ => Palette.Neutral,
    };
    public bool ShowOwner => !IsMine;
    public string OwnerLabel => $"Demande de {OwnerRegimentName}";
    // Boutons de changement de visibilité (créateur / gestionnaire, sur ses propres demandes)
    public bool CanSetRegiment => CanManage && Visibility != ResupplyVisibility.Regiment;
    public bool CanSetAlliance => CanManage && Visibility != ResupplyVisibility.Alliance;
    public bool CanSetPublic => CanManage && Visibility != ResupplyVisibility.Public;

    // --- Plan de production (calculé à la demande) ---
    public IReadOnlyList<CraftStepViewModel> Crafts => _plan.Value.Crafts.Select(c => new CraftStepViewModel(c)).ToList();
    public IReadOnlyList<HarvestLineViewModel> Harvests => _plan.Value.Harvests.Select(h => new HarvestLineViewModel(h)).ToList();
    public bool HasCrafts => _plan.Value.HasCrafts;
    public bool HasHarvests => _plan.Value.HasHarvests;
    public string VehiclesText => $"≈ {_plan.Value.Vehicles} véhicule(s)  ·  {_plan.Value.FinalCrates:N0} caisses à livrer (~15/camion)";
}
