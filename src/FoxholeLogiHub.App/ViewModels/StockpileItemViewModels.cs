using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        ? Palette.GreenDark
        : Palette.Slate;
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
        ? Palette.GreenDark
        : Palette.BrownOpen;
    public bool ShowShareSection => IsOwn && CanManage && !IsPublic && ShareTargets.Count > 0;
    public bool ShowForeignRegiment => !IsOwn;
}

/// <summary>Chargement des icônes d'items (bundle FIR, <c>Data/icons/&lt;code&gt;.png</c>, variante <c>-crated</c>).</summary>
public static class ItemIcons
{
    private static readonly string Dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "icons");

    // Cache : une icône n'est lue qu'une fois sur disque (les cartes en redemandent à chaque refresh).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string code) => Cache.GetOrAdd(code, LoadUncached);

    private static ImageSource? LoadUncached(string code)
    {
        bool crated = code.EndsWith("@crate", StringComparison.Ordinal);
        string baseCode = crated ? code[..^6] : code;

        // 1) code direct (cas normal : import FIR)
        var img = TryLoad(baseCode, crated);
        if (img is not null)
            return img;

        // 2) repli par alias : un code/calibre/nom stocké (« 40mm », « Basic Materials ») → CodeName FIR
        string? alias = FoxholeItemCatalog.AliasToCode(baseCode);
        if (alias is not null && !alias.Equals(baseCode, StringComparison.OrdinalIgnoreCase))
            return TryLoad(alias, crated);

        return null;
    }

    private static ImageSource? TryLoad(string codeName, bool crated)
    {
        foreach (string fileName in crated ? new[] { codeName + "-crated", codeName } : new[] { codeName })
        {
            string path = System.IO.Path.Combine(Dir, fileName + ".png");
            if (!System.IO.File.Exists(path))
                continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}

/// <summary>Libellés FR des catégories FIR (sinon le code brut).</summary>
public static class ItemCategories
{
    public static string Label(string category) => category switch
    {
        "SmallArms" => "Armes légères",
        "HeavyArms" => "Armes lourdes",
        "HeavyAmmo" => "Munitions lourdes",
        "Medical" => "Médical",
        "Supplies" => "Ravitaillement",
        "Uniforms" => "Uniformes",
        "Utility" => "Utilitaire",
        "Parts" => "Pièces",
        "Vehicles" => "Véhicules",
        "" or "Autres" => "Autres",
        _ => category,
    };
}

/// <summary>Une ligne/carte d'item dans le détail d'un stockpile (icône, quantité, statut de stock).</summary>
public sealed class StockpileLineViewModel : ObservableObject
{
    public StockpileLineViewModel(StockpileItemDto dto, bool canManage)
    {
        Code = dto.Code;
        Name = dto.Name;
        Category = dto.Category;
        Quantity = dto.Quantity;
        Low = dto.LowThreshold;
        Critical = dto.CriticalThreshold;
        CanManage = canManage;
        Icon = ItemIcons.Load(Code);
    }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public int Quantity { get; }
    public int Low { get; }
    public int Critical { get; }
    public bool CanManage { get; }
    public ImageSource? Icon { get; }

    public string QuantityText => Quantity.ToString("N0");
    // Catégorie recalculée depuis le code (corrige les items déjà stockés) ; repli sur la catégorie stockée.
    public string CategoryLabel => FoxholeItemCatalog.CategoryOf(Code) ?? ItemCategories.Label(Category);
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    /// <summary>"critical" | "low" | "good" | "" (aucun seuil défini).</summary>
    public string Status =>
        Critical > 0 && Quantity <= Critical ? "critical"
        : Low > 0 && Quantity <= Low ? "low"
        : (Low > 0 || Critical > 0) ? "good"
        : "";

    public bool HasStatus => Status.Length > 0;

    public string StatusLabel => Status switch
    {
        "critical" => "Critique",
        "low" => "Bas",
        "good" => "Bon",
        _ => "",
    };

    public Brush StatusBrush => Status switch
    {
        "critical" => Palette.Critical,
        "low" => Palette.Warning,
        "good" => Palette.Good,
        _ => Palette.Neutral,
    };
}

/// <summary>Une alerte de stock (item sous seuil) affichée dans le tableau de bord.</summary>
public sealed class StockpileAlertViewModel : ObservableObject
{
    public StockpileAlertViewModel(StockpileAlertDto a)
    {
        Code = a.Code;
        Name = a.Name;
        Category = a.Category;
        StockpileName = a.StockpileName;
        RegimentName = a.RegimentName;
        IsOwn = a.IsOwn;
        Hex = a.Hex;
        Town = a.Town;
        Quantity = a.Quantity;
        Low = a.LowThreshold;
        Critical = a.CriticalThreshold;
        Severity = a.Severity;
        Icon = ItemIcons.Load(Code);
    }

    public string Code { get; }
    public string Name { get; }
    public string Category { get; }
    public string StockpileName { get; }
    public string RegimentName { get; }
    public bool IsOwn { get; }
    public string Hex { get; }
    public string Town { get; }
    public int Quantity { get; }
    public int Low { get; }
    public int Critical { get; }
    public string Severity { get; }
    public ImageSource? Icon { get; }

    public bool IsCritical => Severity == "critical";
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string QuantityText => Quantity.ToString("N0");
    public string CategoryLabel => FoxholeItemCatalog.CategoryOf(Code) ?? ItemCategories.Label(Category);

    // Regroupement par stockpile (Dashboard) + tri interne par sévérité.
    public string StockpileGroup
    {
        get
        {
            string loc = string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}";
            string place = string.IsNullOrEmpty(loc) ? StockpileName : $"{StockpileName} — {loc}";
            return IsOwn ? place : $"{place}  ({RegimentName})";
        }
    }
    public int SeverityRank => IsCritical ? 0 : 1;
    public string SeverityGroup => IsCritical ? "🔴 Critique" : "🟠 Bas";
    public string SeverityLabel => IsCritical ? "Critique" : "Bas";
    public string ThresholdText => IsCritical ? $"seuil critique ≤ {Critical:N0}" : $"seuil bas ≤ {Low:N0}";

    public string LocationLabel
    {
        get
        {
            string loc = string.IsNullOrEmpty(Town) ? Hex : $"{Hex} · {Town}";
            string place = $"{StockpileName} — {loc}";
            return IsOwn ? place : $"{place}  ({RegimentName})";
        }
    }

    public Brush SeverityBrush => IsCritical
        ? Palette.Critical
        : Palette.Warning;
}

/// <summary>Un ingrédient d'une recette (icône + nom + quantité).</summary>
public sealed class IngredientViewModel
{
    public IngredientViewModel(ItemIngredient ing)
    {
        Code = ing.Code;
        Qty = ing.Qty;
        var e = FoxholeItemCatalog.Get(ing.Code);
        Name = e?.Name ?? ing.Code;
        Icon = ItemIcons.Load(e?.Code ?? ing.Code);
    }

    public string Code { get; }
    public int Qty { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public string QtyText => $"×{Qty:N0}";
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}

/// <summary>Une recette affichée (bâtiment(s), ingrédients, sortie, temps).</summary>
public sealed class RecipeViewModel
{
    public RecipeViewModel(ItemRecipe r)
    {
        Buildings = string.Join(" · ", r.Buildings);
        Output = r.Output;
        Time = r.Time;
        Power = r.Power;
        IsMpf = r.Buildings.Any(b => b.Contains("MPF"));
        foreach (var ing in r.Ingredients)
            Ingredients.Add(new IngredientViewModel(ing));
    }

    public string Buildings { get; }
    public int Output { get; }
    public double Time { get; }
    public double Power { get; }
    public bool IsMpf { get; }
    public ObservableCollection<IngredientViewModel> Ingredients { get; } = new();

    public string Header => $"🏭 {Buildings}";
    public bool HasOutput => Output > 0;
    public string OutputText => Output > 0 ? $"→ produit {Output:N0}" : "";
    public bool HasMeta => Time > 0 || Power > 0;
    public string MetaText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Time > 0) parts.Add("⏱ " + FormatTime(Time));
            if (Power > 0) parts.Add($"⚡ {Power:0.##} MW");
            return string.Join("    ", parts);
        }
    }

    private static string FormatTime(double s)
    {
        if (s <= 0) return "";
        if (s < 1) return $"{s:0.##} s";
        if (s < 60) return $"{s:0.#} s";
        int m = (int)(s / 60), sec = (int)System.Math.Round(s % 60);
        return sec == 0 ? $"{m} min" : $"{m} min {sec} s";
    }
}

/// <summary>Fiche détaillée d'un item : catégorie, caisse, recettes de craft (gestion poussée).</summary>
public sealed class ItemDetailViewModel : ObservableObject
{
    public ItemDetailViewModel(ItemEntry e)
    {
        Code = e.Code;
        Name = e.Name;
        Caliber = e.Display;
        Category = e.Category;
        CrateSize = e.CrateSize;
        Raw = e.Raw;
        HasRecipe = e.HasRecipe;
        Icon = ItemIcons.Load(e.Code);
        foreach (var r in e.Recipes)
            Recipes.Add(new RecipeViewModel(r));
    }

    public string Code { get; }
    public string Name { get; }
    public string Caliber { get; }
    public string Category { get; }
    public int CrateSize { get; }
    public bool Raw { get; }
    public bool HasRecipe { get; }
    public ImageSource? Icon { get; }
    public ObservableCollection<RecipeViewModel> Recipes { get; } = new();

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public bool HasCaliber => Caliber.Length > 0 && !Caliber.Equals(Name, System.StringComparison.OrdinalIgnoreCase);
    public string CaliberLabel => HasCaliber ? $"Calibre : {Caliber}" : "";

    public bool IsCratable => CrateSize > 0;
    public string CrateText => IsCratable ? $"{CrateSize:N0} / caisse" : "Non caissable";

    public bool NoRecipe => !HasRecipe;
    public bool HasMpf => Recipes.Any(r => r.IsMpf);
    public string RecipeHeader => Recipes.Count > 1 ? $"Recettes ({Recipes.Count})" : "Recette";
    public string NoRecipeText => Raw
        ? "Ressource brute — extraite sur le terrain (champ / mine), non fabriquée."
        : "Produit en raffinerie / installation (recette non répertoriée).";
}
