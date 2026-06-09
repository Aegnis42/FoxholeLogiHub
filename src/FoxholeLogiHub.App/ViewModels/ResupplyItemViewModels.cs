using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

public sealed record ResupplyPriorityOption(int Value, string Label);

/// <summary>Un manque détecté (item sous son seuil bas) → base d'une demande de ravitaillement.</summary>
public sealed class ResupplyNeedViewModel : ObservableObject
{
    public ResupplyNeedViewModel(StockpileAlertDto a)
    {
        Code = a.Code;
        Name = a.Name;
        Category = a.Category;
        StockpileId = a.StockpileId;
        StockpileName = a.StockpileName;
        Hex = a.Hex;
        Town = a.Town;
        Quantity = a.Quantity;
        // Déficit pour repasser au-dessus du seuil bas (sinon, sous le seuil critique seul, vise le critique).
        int target = a.LowThreshold > 0 ? a.LowThreshold : a.CriticalThreshold;
        Deficit = System.Math.Max(0, target - a.Quantity);
        IsCritical = a.Severity == "critical";
        Icon = ItemIcons.Load(Code);
    }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public string StockpileId { get; }
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
        get
        {
            string loc = string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}";
            return $"{StockpileName} — {loc}";
        }
    }
    public Brush DeficitBrush => IsCritical
        ? new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x3A))
        : new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x2E));
}

/// <summary>Une demande de ravitaillement (carte) avec son workflow.</summary>
public sealed class ResupplyRequestViewModel : ObservableObject
{
    public ResupplyRequestViewModel(ResupplyRequestDto d)
    {
        Id = d.Id;
        Code = d.Code;
        Name = d.Name;
        Category = d.Category;
        Quantity = d.Quantity;
        StockpileName = d.StockpileName;
        Hex = d.Hex;
        Town = d.Town;
        Priority = d.Priority;
        Status = d.Status;
        Note = d.Note;
        CreatedByName = d.CreatedByName;
        ClaimedByName = d.ClaimedByName;
        CanManage = d.CanManage;
        MineClaim = d.MineClaim;
        Icon = ItemIcons.Load(Code);
    }

    public string Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public int Quantity { get; }
    public string StockpileName { get; }
    public string Hex { get; }
    public string Town { get; }
    public int Priority { get; }
    public string Status { get; }
    public string Note { get; }
    public string CreatedByName { get; }
    public string ClaimedByName { get; }
    public bool CanManage { get; }
    public bool MineClaim { get; }
    public ImageSource? Icon { get; }

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string QuantityText => Quantity.ToString("N0");
    public bool IsDone => Status == ResupplyStatus.Done;
    public bool IsOpen => !IsDone;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public bool HasClaim => !string.IsNullOrWhiteSpace(ClaimedByName);

    public string LocationLabel
    {
        get
        {
            if (string.IsNullOrEmpty(StockpileName))
                return "Lieu non précisé";
            string loc = string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}";
            return $"{StockpileName} — {loc}";
        }
    }

    public string PriorityLabel => Priority switch
    {
        ResupplyPriority.Urgent => "Urgente",
        ResupplyPriority.High => "Haute",
        _ => "Normale",
    };
    public bool ShowPriority => Priority > 0;
    public Brush PriorityBrush => Priority switch
    {
        ResupplyPriority.Urgent => new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x3A)),
        ResupplyPriority.High => new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x2E)),
        _ => new SolidColorBrush(Color.FromRgb(0x3A, 0x41, 0x4C)),
    };

    public string StatusLabel => Status switch
    {
        ResupplyStatus.Done => "Livrée",
        ResupplyStatus.Claimed => "Pris en charge",
        _ => "Ouverte",
    };
    public Brush StatusBrush => Status switch
    {
        ResupplyStatus.Done => new SolidColorBrush(Color.FromRgb(0x3A, 0x8A, 0x4F)),
        ResupplyStatus.Claimed => new SolidColorBrush(Color.FromRgb(0x2A, 0x6E, 0x8F)),
        _ => new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x2A)),
    };

    public string CreatedInfo => $"par {CreatedByName}";
    public string ClaimInfo => HasClaim ? $"Pris par {ClaimedByName}" : "";

    // Bouton de prise en charge.
    public string ClaimButtonText => MineClaim ? "✓ Je m'en occupe" : (HasClaim ? "Reprendre" : "Prendre en charge");
    public Brush ClaimButtonBrush => MineClaim
        ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x43))
        : new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x45));
}
