using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Libellés/listes pour les stockpiles (types FR, hexagones Foxhole).</summary>
public static class StockpileCatalog
{
    public static readonly (string Value, string Label)[] Types =
    {
        (StockpileTypes.StorageDepot, "Dépôt"),
        (StockpileTypes.Seaport, "Port"),
        (StockpileTypes.Factory, "Usine"),
        (StockpileTypes.MassProductionFactory, "MPF"),
        (StockpileTypes.Refinery, "Raffinerie"),
        (StockpileTypes.ProductionBase, "Base de prod."),
    };

    public static string Label(string type) => Types.FirstOrDefault(t => t.Value == type).Label ?? type;

    public static readonly string[] Hexes =
    {
        "Deadlands", "Callahan's Passage", "Marban Hollow", "Umbral Wildwood", "The Moors",
        "Loch Mór", "The Linn of Mercy", "Reaching Trail", "Stonecradle", "Allod's Bight",
        "The Heartlands", "Westgate", "Origin", "Howl County", "Viper Pit", "Fishermans Row",
        "Stlican Shelf", "Farranac Coast", "The Oarbreaker Isles", "Great March", "Tempest Island",
        "Godcrofts", "Endless Shore", "Ash Fields", "Terminus", "The Clahstra", "The Drowned Vale",
        "Shackled Chasm", "Acrithia", "Red River", "Kalokai", "Nevish Line", "Clanshead Valley",
        "Morgen's Crossing", "Weathered Expanse", "Speaking Woods", "Basin Sionnach", "Sableport",
        "Kings Cage", "Mooring County", "Callum's Cape", "Stema Landing", "The Fingers",
    };
}

/// <summary>Une cible de partage (régiment allié) pour un stockpile privé.</summary>
public sealed class StockpileShareTargetViewModel : ObservableObject
{
    private bool _isShared;
    public StockpileShareTargetViewModel(string stockpileId, string regimentId, string name, bool shared)
    {
        StockpileId = stockpileId;
        RegimentId = regimentId;
        RegimentName = name;
        _isShared = shared;
    }

    public string StockpileId { get; }
    public string RegimentId { get; }
    public string RegimentName { get; }
    public bool IsShared { get => _isShared; set { Set(ref _isShared, value); Raise(nameof(ButtonText)); Raise(nameof(ButtonBrush)); } }

    public string ButtonText => IsShared ? $"✓ {RegimentName}" : RegimentName;
    public Brush ButtonBrush => IsShared
        ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x43))
        : new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x45));
}

/// <summary>Un stockpile affiché dans la liste.</summary>
public sealed class StockpileItemViewModel : ObservableObject
{
    public StockpileItemViewModel(StockpileDto dto, IReadOnlyList<(string Id, string Name)> alliances)
    {
        Id = dto.Id;
        Name = dto.Name;
        Hex = dto.Hex;
        Town = dto.Town;
        Type = dto.Type;
        Code = dto.Code;
        IsPublic = dto.IsPublic;
        IsOwn = dto.IsOwn;
        CanManage = dto.CanManage;
        RegimentName = dto.RegimentName;

        if (IsOwn && CanManage && !IsPublic)
            foreach (var a in alliances)
                ShareTargets.Add(new StockpileShareTargetViewModel(Id, a.Id, a.Name, dto.SharedRegimentIds.Contains(a.Id)));
    }

    public string Id { get; }
    public string Name { get; }
    public string Hex { get; }
    public string Town { get; }
    public string Type { get; }
    public string Code { get; }
    public bool IsPublic { get; }
    public bool IsOwn { get; }
    public bool CanManage { get; }
    public string RegimentName { get; }

    public ObservableCollection<StockpileShareTargetViewModel> ShareTargets { get; } = new();

    public string TypeLabel => StockpileCatalog.Label(Type);
    public string LocationLabel => string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}";
    public bool HasCode => !string.IsNullOrEmpty(Code);
    public string VisibilityLabel => IsPublic ? "Public" : "Privé";
    public Brush VisibilityBrush => IsPublic
        ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x43))
        : new SolidColorBrush(Color.FromRgb(0x5A, 0x4A, 0x2A));
    public bool ShowShareSection => IsOwn && CanManage && !IsPublic && ShareTargets.Count > 0;
    public bool ShowForeignRegiment => !IsOwn;
}

/// <summary>Une ligne d'item dans le détail d'un stockpile.</summary>
public sealed class StockpileLineViewModel : ObservableObject
{
    public StockpileLineViewModel(StockpileItemDto dto, bool canManage)
    {
        Code = dto.Code;
        Name = dto.Name;
        Category = dto.Category;
        Quantity = dto.Quantity;
        CanManage = canManage;
    }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public int Quantity { get; }
    public bool CanManage { get; }

    public string QuantityText => Quantity.ToString("N0");
    public string CategoryLabel => string.IsNullOrEmpty(Category) ? "Autres" : Category;
}
