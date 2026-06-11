using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Un hexagone du monde sur la carte (position canvas = coin haut-gauche de sa boîte).</summary>
public sealed class MapHexViewModel : ObservableObject
{
    private bool _isSelected;

    public MapHexViewModel(string map, string display, double x, double y, double w, double h)
    {
        Map = map;
        Display = display;
        X = x; Y = y; W = w; H = h;
        // Hexagone « flat-top » dans sa boîte englobante (W × H).
        Points = new PointCollection
        {
            new Point(0.25 * W, 0), new Point(0.75 * W, 0), new Point(W, H / 2),
            new Point(0.75 * W, H), new Point(0.25 * W, H), new Point(0, H / 2),
        };
        Points.Freeze();

        // Géométrie de découpe pour le fond de carte (la tuile est rectangulaire).
        var clip = new StreamGeometry();
        using (var ctx = clip.Open())
        {
            ctx.BeginFigure(Points[0], true, true);
            ctx.PolyLineTo(Points.Skip(1).ToList(), true, true);
        }
        clip.Freeze();
        HexClip = clip;
    }

    private ImageSource? _tile;

    /// <summary>Fond de carte officiel (terrain) — null tant que la tuile n'est pas en cache.</summary>
    public ImageSource? Tile => _tile;
    public Geometry HexClip { get; }

    public void SetTile(ImageSource? tile)
    {
        _tile = tile;
        Raise(nameof(Tile));
    }

    public string Map { get; }
    public string Display { get; }
    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }
    public PointCollection Points { get; }

    public List<WarMapTownDto> Towns { get; } = new();
    public List<WarMapStructDto> Structures { get; } = new();

    /// <summary>Sous-régions (zone d'influence de chaque ville, Voronoï découpé dans l'hexagone).</summary>
    public ObservableCollection<MapCellViewModel> Cells { get; } = new();

    /// <summary>Rempli seulement quand l'hexagone n'a pas de sous-régions (sinon les cellules colorent).</summary>
    public Brush? Fill => Cells.Count > 0 ? null : Palette.MapNeutral;

    public bool IsSelected { get => _isSelected; set { Set(ref _isSelected, value); Raise(nameof(Stroke)); Raise(nameof(StrokeThickness)); } }
    public Brush Stroke => IsSelected ? Brushes.White : Palette.MapStroke;

    /// <summary>Facteur de contre-échelle des bordures (poussé par la carte à chaque zoom).</summary>
    public static double StrokeScale = 1.0;
    public double StrokeThickness => (IsSelected ? 2.2 : 1.3) * StrokeScale;

    public void RaiseFill() => Raise(nameof(Fill));
    public void RaiseStroke() => Raise(nameof(StrokeThickness));
}

/// <summary>Une sous-région : la zone d'influence d'une ville dans son hexagone (coordonnées locales).</summary>
public sealed class MapCellViewModel
{
    public MapCellViewModel(MapHexViewModel hex, WarMapTownDto town, PointCollection points)
    {
        Hex = hex;
        Town = town;
        Points = points;
        Fill = town.Scorched ? Palette.CellScorched
            : town.Team == "WARDENS" ? Palette.CellWarden
            : town.Team == "COLONIALS" ? Palette.CellColonial
            : Palette.CellNeutral;
    }

    public MapHexViewModel Hex { get; }
    public WarMapTownDto Town { get; }
    public PointCollection Points { get; }
    public Brush Fill { get; }
    public string Tooltip => (Town.Tier > 0 ? new string('★', Town.Tier) + " " : "")
        + $"{Town.Name} — " + (Town.Scorched ? "rasée 🔥"
        : Town.Team == "WARDENS" ? "Wardens"
        : Town.Team == "COLONIALS" ? "Colonials" : "neutre");
}

/// <summary>Une structure logistique affichée quand un hexagone est sélectionné (position canvas absolue).</summary>
public sealed class MapStructViewModel
{
    private static readonly Dictionary<int, (string Glyph, string Label)> Types = new()
    {
        [11] = ("🏥", "Hôpital"),
        [12] = ("🚛", "Usine de véhicules"),
        [17] = ("🛢", "Raffinerie"),
        [18] = ("🚢", "Chantier naval"),
        [19] = ("🔬", "Centre technologique"),
        [27] = ("🏰", "Fortin"),
        [33] = ("🏬", "Dépôt de stockage"),
        [34] = ("🏭", "Usine"),
        [39] = ("🔨", "Chantier de construction"),
        [45] = ("⛪", "Base relique"),
        [46] = ("⛪", "Base relique"),
        [47] = ("⛪", "Base relique"),
        [51] = ("⚙", "MPF"),
        [52] = ("⚓", "Port"),
        [88] = ("✈", "Dépôt d'aéronefs"),
        [89] = ("🛩", "Usine d'aéronefs"),
        [91] = ("🛫", "Piste d'aviation"),
        [92] = ("🛫", "Piste d'aviation T2"),
        [20] = ("♻", "Champ de ferraille"),
        [21] = ("🔩", "Champ de composants"),
        [22] = ("⛽", "Champ de carburant"),
        [23] = ("🟡", "Champ de soufre"),
        [32] = ("🟡", "Mine de soufre"),
        [38] = ("♻", "Mine de ferraille"),
        [40] = ("🔩", "Mine de composants"),
        [61] = ("⚫", "Champ de charbon"),
        [62] = ("🛢", "Champ de pétrole"),
        [75] = ("🛢", "Plateforme pétrolière"),
    };

    /// <summary>Champ ou mine de ressource (récolte) — filtrable via le toggle « ⛏ Ressources ».</summary>
    public static bool IsResourceIcon(int icon) => icon is 20 or 21 or 22 or 23 or 32 or 38 or 40 or 61 or 62 or 75;
    public bool IsResource => IsResourceIcon(Icon);

    /// <summary>Mis en évidence par « Où produire ? » (anneau doré).</summary>
    public bool IsHighlighted { get; init; }

    public MapStructViewModel(MapHexViewModel hex, WarMapStructDto s)
    {
        Hex = hex;
        Icon = s.Icon;
        RelX = s.X;
        RelY = s.Y;
        var (glyph, label) = Types.TryGetValue(s.Icon, out var t) ? t : ("•", $"Structure {s.Icon}");
        Glyph = glyph;
        Label = label;
        // Point d'ancrage monde exact — le visuel se centre lui-même dans le template
        // (contre-échelle : les marqueurs gardent une taille écran constante).
        X = hex.X + s.X * hex.W;
        Y = hex.Y + s.Y * hex.H;
        TeamBrush = s.Team == "WARDENS" ? Palette.Wardens
            : s.Team == "COLONIALS" ? Palette.Colonials
            : Palette.MapTownNeutral;
        Tooltip = $"{label} — " + (s.Team == "WARDENS" ? "Wardens" : s.Team == "COLONIALS" ? "Colonials" : "neutre");
    }

    public MapHexViewModel Hex { get; }
    public int Icon { get; }
    public double RelX { get; }
    public double RelY { get; }
    public string Glyph { get; }
    public string Label { get; }
    public double X { get; }
    public double Y { get; }
    public Brush TeamBrush { get; }
    public string Tooltip { get; }

    /// <summary>Port ou dépôt de stockage : on peut y rattacher un stockpile d'un clic.</summary>
    public bool CanHostStockpile => Icon is 33 or 52;
    public string HostType => Icon == 52 ? StockpileTypes.Seaport : StockpileTypes.StorageDepot;
}

/// <summary>Un point « ville » sur la carte (position canvas absolue).</summary>
public sealed class MapTownViewModel
{
    public MapTownViewModel(MapHexViewModel hex, WarMapTownDto t)
    {
        Hex = hex;
        Name = t.Name;
        Team = t.Team;
        Scorched = t.Scorched;
        Tier = t.Tier;
        X = hex.X + t.X * hex.W;
        Y = hex.Y + t.Y * hex.H;
        Fill = t.Scorched ? Palette.Critical
            : t.Team == "WARDENS" ? Palette.Wardens
            : t.Team == "COLONIALS" ? Palette.Colonials
            : Palette.MapTownNeutral;
    }

    public MapHexViewModel Hex { get; }
    public string Name { get; }
    public string Team { get; }
    public bool Scorched { get; }
    public int Tier { get; }
    public double X { get; }
    public double Y { get; }
    public Brush Fill { get; }

    public string TierStars => Tier > 0 ? new string('★', Tier) : "";

    public string TeamLabel => Scorched ? "rasée 🔥" : Team switch
    {
        "WARDENS" => "Wardens",
        "COLONIALS" => "Colonials",
        _ => "neutre",
    };
    public string Tooltip => (TierStars.Length > 0 ? TierStars + " " : "") + $"{Name} — {TeamLabel}";
    public string PanelName => (TierStars.Length > 0 ? TierStars + " " : "") + Name;
}

/// <summary>Un pin « stockpile » sur la carte. X/Y mutables : suivent le curseur pendant un drag.</summary>
public sealed class MapPinViewModel : ObservableObject
{
    private double _x, _y;

    public MapPinViewModel(StockpileItemViewModel s, MapHexViewModel hex, double x, double y)
    {
        Stockpile = s;
        Hex = hex;
        _x = x; _y = y;
    }

    public StockpileItemViewModel Stockpile { get; }
    public MapHexViewModel Hex { get; }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public bool CanDrag => Stockpile.CanManage;
    public bool IsThreatened => Stockpile.IsThreatened;

    public string Glyph => Stockpile.Type switch
    {
        StockpileTypes.Bunker => "🛡",
        StockpileTypes.ProductionBase => "🏗",
        StockpileTypes.Factory => "🏭",
        StockpileTypes.MassProductionFactory => "⚙",
        StockpileTypes.Refinery => "🛢",
        StockpileTypes.Seaport => "⚓",
        _ => "📦",
    };

    public string Tooltip => $"{Stockpile.Name} ({Stockpile.TypeLabel}) — {Stockpile.LocationLabel}"
        + (Stockpile.IsThreatened ? $"\n{Stockpile.ThreatLabel}" : "");
}

/// <summary>Demande de placement venant de l'onglet Stockpiles (création hors port/dépôt).</summary>
public sealed record PendingMapPick(string Name, string Hex, string Town, string Type, string Code, bool IsPublic);

/// <summary>
/// Carte interactive du monde : 53 hexagones (HexLayout) colorés par le contrôle réel des villes
/// (API War, cache serveur), points de villes, pins des stockpiles visibles, panneau de détail.
/// </summary>
public sealed class MapViewModel : ObservableObject
{
    private const double S = 70;                       // demi-largeur d'un hexagone (px canvas)
    private const double Margin = 30;
    private static readonly double H = Math.Sqrt(3) * S;

    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();
    private readonly MapTileService _tiles = new();
    private StockpileClient? _client;
    private string _clientKey = "";
    private bool _tilesStarted;
    private StockpilesViewModel? _stockpiles;
    private ResupplyViewModel? _resupply;

    private bool _authed;
    private string _status = "";
    private bool _retryPending;
    private bool _deepZoom;
    private double _viewScale = 1.0;
    private MapHexViewModel? _selected;

    public ObservableCollection<MapHexViewModel> Hexes { get; } = new();
    public ObservableCollection<MapTownViewModel> Towns { get; } = new();
    public ObservableCollection<MapPinViewModel> Pins { get; } = new();

    // Couches d'habillage de la carte : en zoom profond elles couvrent TOUS les hexagones
    // visibles, sinon seulement l'hexagone sélectionné.
    public ObservableCollection<MapTownViewModel> TownLabels { get; } = new();
    public ObservableCollection<MapStructViewModel> MapStructures { get; } = new();

    public ObservableCollection<MapTownViewModel> SelectedTowns { get; } = new();
    public ObservableCollection<string> SelectedStructureSummary { get; } = new();
    public ObservableCollection<StockpileItemViewModel> SelectedStockpiles { get; } = new();
    public ObservableCollection<string> SelectedRequests { get; } = new();

    public double CanvasWidth { get; private set; }
    public double CanvasHeight { get; private set; }

    public bool Authed { get => _authed; private set { Set(ref _authed, value); Raise(nameof(ShowAuthNeeded)); } }
    public bool ShowAuthNeeded => !Authed;
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string SelectedName => _selected?.Display ?? "";
    public bool HasSelection => _selected is not null;
    public bool NoSelection => _selected is null;
    public bool SelectedHasStockpiles => SelectedStockpiles.Count > 0;
    public bool SelectedHasRequests => SelectedRequests.Count > 0;
    public bool SelectedHasStructures => SelectedStructureSummary.Count > 0;

    /// <summary>Épaisseur des bissectrices de zones, contre-échelonnée (≈1 px écran).</summary>
    public double CellStrokeThickness => 0.9 * Math.Min(1.0 / _viewScale, 3.0);

    /// <summary>
    /// Appelé par la vue à chaque changement de zoom : ajuste les épaisseurs de traits et bascule
    /// les couches labels/structures en mode « zoom profond » (tous les hexagones) au-delà de 1.6×.
    /// </summary>
    private double _lastStrokeScale = -1;

    public void SetViewScale(double scale)
    {
        _viewScale = Math.Max(scale, 0.01);

        // Les épaisseurs de traits ne sont recalculées que si le zoom a bougé de > 4 % :
        // chaque recalcul notifie ~53 bordures + ~430 bissectrices (bindings), inutile à
        // chaque cran de molette pour une variation invisible à l'écran.
        if (Math.Abs(_viewScale - _lastStrokeScale) / Math.Max(_lastStrokeScale, 0.01) > 0.04)
        {
            _lastStrokeScale = _viewScale;
            Raise(nameof(CellStrokeThickness));
            MapHexViewModel.StrokeScale = Math.Min(1.0 / _viewScale, 2.6);
            foreach (var hex in Hexes)
                hex.RaiseStroke();
        }

        bool deep = scale >= 1.6;
        if (deep != _deepZoom)
        {
            _deepZoom = deep;
            RebuildOverlays();
        }
    }

    private bool _showResources = true;

    /// <summary>Affiche/masque les champs et mines de ressources sur la carte.</summary>
    public bool ShowResources
    {
        get => _showResources;
        set
        {
            if (_showResources == value)
                return;
            Set(ref _showResources, value);
            RebuildOverlays();
        }
    }

    /// <summary>Labels de villes + structures : tout le monde en zoom profond, sinon l'hexagone sélectionné.</summary>
    private void RebuildOverlays()
    {
        TownLabels.Clear();
        MapStructures.Clear();
        if (_deepZoom)
        {
            foreach (var t in Towns)
                TownLabels.Add(t);
            foreach (var hex in Hexes)
                AddStructures(hex);
        }
        else if (_selected is not null)
        {
            foreach (var t in Towns.Where(t => t.Hex == _selected))
                TownLabels.Add(t);
            AddStructures(_selected);
        }
    }

    private void AddStructures(MapHexViewModel hex)
    {
        foreach (var s in hex.Structures)
        {
            bool highlighted = _highlightIcons?.Contains(s.Icon) == true;
            if (!highlighted && !_showResources && MapStructViewModel.IsResourceIcon(s.Icon))
                continue;
            MapStructures.Add(new MapStructViewModel(hex, s) { IsHighlighted = highlighted });
        }
    }

    // ---------- Mesure de distance ----------

    // Un hexagone Foxhole fait ≈ 2,2 km pointe-à-pointe ; notre hexagone canvas fait 2·S de large.
    private const double MetersPerCanvasUnit = 2196.0 / (2 * S);
    private const double TruckKmh = 25.0; // camion sur route, estimation prudente

    private bool _measureActive;
    private Point? _measureA;
    private bool _measureLocked;

    public bool MeasureActive
    {
        get => _measureActive;
        set
        {
            if (_measureActive == value)
                return;
            Set(ref _measureActive, value);
            _measureA = null;
            _measureLocked = false;
            MeasureVisible = false;
            Raise(nameof(MeasureVisible));
        }
    }

    public bool MeasureVisible { get; private set; }
    public double MeasureX1 { get; private set; }
    public double MeasureY1 { get; private set; }
    public double MeasureX2 { get; private set; }
    public double MeasureY2 { get; private set; }
    public double MeasureLabelX { get; private set; }
    public double MeasureLabelY { get; private set; }
    public string MeasureLabel { get; private set; } = "";

    /// <summary>Clic en mode mesure : 1er = point A, 2e = verrouille, 3e = nouvelle mesure.</summary>
    public void MeasureClick(Point canvas)
    {
        if (_measureA is null || _measureLocked)
        {
            _measureA = canvas;
            _measureLocked = false;
            UpdateMeasure(canvas, canvas);
        }
        else
        {
            _measureLocked = true;
            UpdateMeasure(_measureA.Value, canvas);
        }
    }

    /// <summary>Survol en mode mesure : le point B suit le curseur tant que la mesure n'est pas verrouillée.</summary>
    public void MeasureHover(Point canvas)
    {
        if (_measureA is not null && !_measureLocked)
            UpdateMeasure(_measureA.Value, canvas);
    }

    private void UpdateMeasure(Point a, Point b)
    {
        MeasureX1 = a.X; MeasureY1 = a.Y;
        MeasureX2 = b.X; MeasureY2 = b.Y;
        MeasureLabelX = (a.X + b.X) / 2;
        MeasureLabelY = (a.Y + b.Y) / 2;

        double meters = (b - a).Length * MetersPerCanvasUnit;
        double minutes = meters / 1000.0 / TruckKmh * 60.0;
        MeasureLabel = meters < 1000
            ? $"≈ {meters:0} m"
            : $"≈ {meters / 1000.0:0.0} km · ~{minutes:0} min 🚚";

        MeasureVisible = true;
        Raise(nameof(MeasureVisible));
        Raise(nameof(MeasureX1)); Raise(nameof(MeasureY1));
        Raise(nameof(MeasureX2)); Raise(nameof(MeasureY2));
        Raise(nameof(MeasureLabelX)); Raise(nameof(MeasureLabelY));
        Raise(nameof(MeasureLabel));
    }

    // ---------- « Où produire ? » (surbrillance des lieux du plan de production) ----------

    private HashSet<int>? _highlightIcons;

    /// <summary>Met en évidence les structures utiles au plan (usines, raffineries, champs…) et zoome sur l'hexagone de la demande.</summary>
    public void HighlightProduction(string hexFreeText, IReadOnlyCollection<int> icons)
    {
        _highlightIcons = icons.Count > 0 ? new HashSet<int>(icons) : null;
        var hex = FindHex(hexFreeText);
        if (hex is not null)
        {
            Select(hex); // RefreshSelection → RebuildOverlays (applique la surbrillance)
            FocusHexRequested?.Invoke(hex);
        }
        else
        {
            RebuildOverlays();
        }
        Status = _highlightIcons is not null
            ? "Lieux de production et de récolte du plan en surbrillance ✨ (« Vue monde » pour effacer)"
            : Status;
    }

    public void ClearHighlight()
    {
        if (_highlightIcons is null)
            return;
        _highlightIcons = null;
        RebuildOverlays();
    }

    public void Initialize(StockpilesViewModel stockpiles, ResupplyViewModel resupply)
    {
        _stockpiles = stockpiles;
        _resupply = resupply;
        BuildHexes(); // la géométrie ne dépend pas des données — dessinée dès le départ
    }

    public void ClearAuth()
    {
        _client?.Dispose(); _client = null;
        _clientKey = "";
        Authed = false;
        Towns.Clear();
        Pins.Clear();
        CancelPlacement();
        CancelPick();
        Select(null);
        Status = "Connecte-toi avec Steam.";
    }

    public async Task RefreshAsync()
    {
        string? token = _tokenStore.Load();
        if (token is null) { ClearAuth(); return; }

        try
        {
            string baseUrl = _settingsStore.Load().ApiBaseUrl;
            string clientKey = $"{baseUrl}|{token}";
            if (_client is null || _clientKey != clientKey)
            {
                _client?.Dispose();
                _client = new StockpileClient(baseUrl, token);
                _clientKey = clientKey;
            }
            Authed = true;

            var map = await _client.GetWarMapAsync();
            ApplyControl(map);
            BuildPins();
            RefreshSelection();
            StartTileDownload();
            Status = map is { Available: true }
                ? $"{Towns.Count} villes · {Pins.Count} stockpile(s) · molette = zoom, glisser = déplacer, clic = détail"
                : "Données de guerre en cours de chargement…";

            // Cache serveur encore froid (juste après un déploiement) : on retentera tout seul.
            if (map is not { Available: true } && !_retryPending)
            {
                _retryPending = true;
                _ = RetryLaterAsync();
            }
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
    }

    private async Task RetryLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(45));
        _retryPending = false;
        if (Authed)
            await RefreshAsync();
    }

    /// <summary>
    /// Télécharge les fonds de carte officiels en arrière-plan (une fois ; ~190 Mo au premier
    /// lancement, puis tout vient du cache disque) et les applique en vignettes 512 px.
    /// </summary>
    private void StartTileDownload()
    {
        if (_tilesStarted)
            return;
        _tilesStarted = true;
        _ = Task.Run(async () =>
        {
            int done = 0, missing = 0;
            var hexes = Hexes.ToList();
            var tasks = hexes.Select(async hex =>
            {
                string? path = await _tiles.EnsureAsync(hex.Map);
                if (path is null)
                {
                    Interlocked.Increment(ref missing);
                    return;
                }
                var thumb = MapTileService.LoadImage(path, 512);
                hex.SetTile(thumb);
                int n = Interlocked.Increment(ref done);
                if (n % 8 == 0 && n < hexes.Count)
                    Status = $"Fonds de carte : {n}/{hexes.Count}…";
            }).ToList();
            await Task.WhenAll(tasks);
            if (missing > 0)
                _tilesStarted = false; // réseau coupé ? on retentera au prochain refresh
        });
    }

    /// <summary>Charge la tuile pleine résolution pour l'hexagone zoomé.</summary>
    private async Task LoadHiResTileAsync(MapHexViewModel hex)
    {
        string? path = await _tiles.EnsureAsync(hex.Map);
        if (path is null)
            return;
        var full = await Task.Run(() => MapTileService.LoadImage(path, 1024));
        if (full is not null)
            hex.SetTile(full);
    }

    // ---------- Construction ----------

    private void BuildHexes()
    {
        if (Hexes.Count > 0)
            return;

        // Centres en coordonnées « monde » : cx = 1.5·P·S, cy = √3·(Q + P/2)·S (flat-top).
        var centers = HexLayout.ByMap
            .Select(kv => (Map: kv.Key, Cx: 1.5 * kv.Value.P * S, Cy: Math.Sqrt(3) * (kv.Value.Q + kv.Value.P / 2.0) * S))
            .ToList();
        double minX = centers.Min(c => c.Cx), minY = centers.Min(c => c.Cy);
        double maxX = centers.Max(c => c.Cx), maxY = centers.Max(c => c.Cy);
        CanvasWidth = maxX - minX + 2 * S + 2 * Margin;
        CanvasHeight = maxY - minY + H + 2 * Margin;
        Raise(nameof(CanvasWidth));
        Raise(nameof(CanvasHeight));

        foreach (var c in centers.OrderBy(c => c.Cy).ThenBy(c => c.Cx))
        {
            double x = c.Cx - minX + Margin;          // coin haut-gauche de la boîte 2S × H
            double y = c.Cy - minY + Margin;
            Hexes.Add(new MapHexViewModel(c.Map, DisplayNameFor(c.Map), x, y, 2 * S, H));
        }
    }

    private void ApplyControl(WarMapDto? map)
    {
        Towns.Clear();
        foreach (var hex in Hexes)
        {
            hex.Towns.Clear();
            hex.Structures.Clear();
            hex.Cells.Clear();
        }
        if (map is not { Available: true })
        {
            foreach (var hex in Hexes)
                hex.RaiseFill();
            return;
        }

        // Clé normalisée (suffixe « Hex » ignoré) : l'API du jeu et la table de positions ne
        // s'accordent pas toujours sur le suffixe (cas Marban Hollow).
        var byMap = Hexes.ToDictionary(h => NormMap(h.Map));
        foreach (var h in map.Hexes)
        {
            if (!byMap.TryGetValue(NormMap(h.Map), out var hex))
                continue;
            hex.Towns.AddRange(h.Towns);
            hex.Structures.AddRange(h.Structures);
            BuildCells(hex);
            foreach (var t in h.Towns)
                Towns.Add(new MapTownViewModel(hex, t));
        }
        foreach (var hex in Hexes)
            hex.RaiseFill();
    }

    /// <summary>
    /// Sous-régions façon FoxholeStats : partition de Voronoï de l'hexagone autour de ses villes
    /// (chaque point appartient à la ville la plus proche), découpée par bissectrices successives.
    /// </summary>
    private static void BuildCells(MapHexViewModel hex)
    {
        hex.Cells.Clear();
        if (hex.Towns.Count == 0)
            return;

        var hexPoly = hex.Points.ToList(); // coordonnées locales (0..W, 0..H)
        var sites = hex.Towns.Select(t => (Town: t, P: new Point(t.X * hex.W, t.Y * hex.H))).ToList();

        foreach (var (town, site) in sites)
        {
            List<Point> cell = hexPoly;
            foreach (var (_, other) in sites)
            {
                if (other == site)
                    continue;
                cell = ClipCloserTo(cell, site, other);
                if (cell.Count < 3)
                    break;
            }
            if (cell.Count < 3)
                continue;
            var pts = new PointCollection(cell);
            pts.Freeze();
            hex.Cells.Add(new MapCellViewModel(hex, town, pts));
        }
    }

    /// <summary>Garde la partie de <paramref name="poly"/> plus proche de <paramref name="site"/> que de <paramref name="other"/> (Sutherland–Hodgman contre la bissectrice).</summary>
    private static List<Point> ClipCloserTo(List<Point> poly, Point site, Point other)
    {
        var result = new List<Point>(poly.Count + 2);
        double mx = (site.X + other.X) / 2, my = (site.Y + other.Y) / 2;
        double nx = other.X - site.X, ny = other.Y - site.Y; // normale orientée vers « other »
        double F(Point p) => (p.X - mx) * nx + (p.Y - my) * ny; // ≤ 0 → côté « site »

        for (int i = 0; i < poly.Count; i++)
        {
            Point prev = poly[(i + poly.Count - 1) % poly.Count];
            Point cur = poly[i];
            double fp = F(prev), fc = F(cur);
            if (fp <= 0 && fc <= 0)
            {
                result.Add(cur);
            }
            else if (fp <= 0 && fc > 0)
            {
                result.Add(Lerp(prev, cur, fp / (fp - fc)));
            }
            else if (fp > 0 && fc <= 0)
            {
                result.Add(Lerp(prev, cur, fp / (fp - fc)));
                result.Add(cur);
            }
        }
        return result;
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private void BuildPins()
    {
        Pins.Clear();
        if (_stockpiles is null)
            return;

        var perHex = new Dictionary<string, int>();
        foreach (var s in _stockpiles.Stockpiles)
        {
            var hex = FindHex(s.Hex);
            if (hex is null)
                continue;

            // Ancrage : la position exacte si posé depuis la carte, sinon la ville, sinon le centre.
            double x, y;
            var town = Towns.FirstOrDefault(t => t.Hex == hex && Norm(t.Name) == Norm(s.Town));
            if (s.MapX is double mx && s.MapY is double my)
            {
                x = hex.X + mx * hex.W;
                y = hex.Y + my * hex.H;
            }
            else if (town is not null)
            {
                x = town.X;
                y = town.Y;
            }
            else
            {
                int n = perHex.GetValueOrDefault(hex.Map);
                x = hex.X + hex.W / 2 + (n % 3) * 16 - 16;
                y = hex.Y + hex.H / 2 + (n / 3) * 18;
            }
            perHex[hex.Map] = perHex.GetValueOrDefault(hex.Map) + 1;
            Pins.Add(new MapPinViewModel(s, hex, x, y));
        }
    }

    // ---------- Choix d'emplacement demandé par l'onglet Stockpiles ----------

    private PendingMapPick? _pick;
    private StockpileItemViewModel? _moveTarget;

    public bool PickActive => _pick is not null || _moveTarget is not null;
    public string PickBanner => _moveTarget is not null
        ? $"📍 Clique le nouvel emplacement de « {_moveTarget.Name} »"
        : _pick is not null ? $"📍 Clique sur la carte pour placer « {_pick.Name} »" : "";

    /// <summary>La vue doit zoomer sur cet hexagone (le ViewModel ne possède pas les transforms).</summary>
    public event Action<MapHexViewModel>? FocusHexRequested;

    /// <summary>Le stockpile demandé par l'onglet Stockpiles a été créé.</summary>
    public event Action? PickCompleted;

    public void BeginPickPosition(PendingMapPick pick)
    {
        CancelPlacement(); // pas deux formulaires en même temps
        _pick = pick;
        Raise(nameof(PickActive));
        Raise(nameof(PickBanner));

        var hex = FindHex(pick.Hex);
        if (hex is not null)
        {
            Select(hex);
            FocusHexRequested?.Invoke(hex);
        }
    }

    public void CancelPick()
    {
        _pick = null;
        _moveTarget = null;
        Raise(nameof(PickActive));
        Raise(nameof(PickBanner));
    }

    /// <summary>Déplacement d'un pin existant : le prochain clic sur la carte le repositionne.</summary>
    public void BeginMovePin(StockpileItemViewModel s)
    {
        CancelPlacement();
        _pick = null;
        _moveTarget = s;
        Raise(nameof(PickActive));
        Raise(nameof(PickBanner));
    }

    /// <summary>Supprime un stockpile depuis la carte (après confirmation côté vue).</summary>
    public async Task DeleteStockpileAsync(StockpileItemViewModel s)
    {
        if (_client is null)
            return;
        try
        {
            await _client.DeleteAsync(s.Id);
            Status = $"« {s.Name} » supprimé.";
            if (_stockpiles is not null)
                await _stockpiles.RefreshAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = $"Suppression impossible : {ex.Message}";
        }
    }

    /// <summary>L'hexagone contenant ce point canvas (test point-dans-polygone), ou null.</summary>
    public MapHexViewModel? HexAt(Point canvas)
    {
        foreach (var hex in Hexes)
        {
            if (canvas.X < hex.X || canvas.X > hex.X + hex.W || canvas.Y < hex.Y || canvas.Y > hex.Y + hex.H)
                continue;
            // Even-odd sur les 6 sommets (coordonnées locales) — les bboxes voisines se chevauchent.
            double px = canvas.X - hex.X, py = canvas.Y - hex.Y;
            bool inside = false;
            var pts = hex.Points;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                if ((pts[i].Y > py) != (pts[j].Y > py)
                    && px < (pts[j].X - pts[i].X) * (py - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                    inside = !inside;
            }
            if (inside)
                return hex;
        }
        return null;
    }

    /// <summary>Déplace un stockpile à une position précise (drag de pin ou mode « déplacer »).</summary>
    public async Task MoveStockpileToAsync(StockpileItemViewModel target, MapHexViewModel hex, double relX, double relY)
    {
        if (_client is null)
            return;
        relX = Math.Clamp(relX, 0, 1);
        relY = Math.Clamp(relY, 0, 1);
        try
        {
            await _client.SetPositionAsync(new SetStockpilePositionRequest(
                target.Id, hex.Display, NearestTownName(hex, relX, relY), relX, relY));
            Status = $"« {target.Name} » déplacé 📍";
            if (_stockpiles is not null)
                await _stockpiles.RefreshAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = $"Déplacement impossible : {ex.Message}";
            await RefreshAsync(); // remet le pin à sa vraie position
        }
    }

    /// <summary>Clic sur la carte en mode choix : crée (ou déplace) le stockpile à cet endroit précis.</summary>
    public async Task CompletePickAsync(MapHexViewModel hex, double relX, double relY)
    {
        if (_client is null)
            return;
        relX = Math.Clamp(relX, 0, 1);
        relY = Math.Clamp(relY, 0, 1);

        if (_moveTarget is not null)
        {
            var target = _moveTarget;
            CancelPick();
            await MoveStockpileToAsync(target, hex, relX, relY);
            Select(hex);
            return;
        }

        if (_pick is null)
            return;
        var pick = _pick;
        string town = pick.Town.Length > 0 ? pick.Town : NearestTownName(hex, relX, relY);
        try
        {
            await _client.CreateAsync(new CreateStockpileRequest(
                pick.Name, hex.Display, town, pick.Type, pick.Code, pick.IsPublic, relX, relY));
            CancelPick();
            Status = $"Stockpile « {pick.Name} » placé 📍";
            PickCompleted?.Invoke();
            if (_stockpiles is not null)
                await _stockpiles.RefreshAsync();
            await RefreshAsync();
            Select(hex);
        }
        catch (Exception ex)
        {
            Status = $"Création impossible : {ex.Message}";
        }
    }

    // ---------- Pose d'un stockpile depuis la carte ----------

    private MapHexViewModel? _placeHex;
    private double _placeRelX, _placeRelY;
    private string _placeType = "";
    private string _placeTown = "";
    private string _placementName = "";
    private string _placementCode = "";

    public bool PlacementActive => _placeHex is not null;
    public string PlacementTitle { get; private set; } = "";
    public string PlacementInfo { get; private set; } = "";
    public string PlacementName { get => _placementName; set => Set(ref _placementName, value); }
    public string PlacementCode { get => _placementCode; set => Set(ref _placementCode, value); }
    public bool PlacementUsesCode => StockpileTypes.UsesCode(_placeType);

    /// <summary>Ouvre le formulaire de pose (clic droit sur la carte ou clic sur un port/dépôt).</summary>
    public void BeginPlacement(MapHexViewModel hex, double relX, double relY, string type)
    {
        _placeHex = hex;
        _placeRelX = Math.Clamp(relX, 0, 1);
        _placeRelY = Math.Clamp(relY, 0, 1);
        _placeType = type;
        _placeTown = NearestTownName(hex, _placeRelX, _placeRelY);

        string typeLabel = StockpileCatalog.Label(type);
        PlacementTitle = $"➕ Nouveau stockpile : {typeLabel}";
        PlacementInfo = hex.Display
            + (_placeTown.Length > 0 ? $" · {_placeTown}" : "")
            + " — privé (visibilité modifiable dans l'onglet Stockpiles).";
        PlacementName = _placeTown.Length > 0 ? $"{typeLabel} — {_placeTown}" : $"{typeLabel} — {hex.Display}";
        PlacementCode = "";
        RaisePlacement();
        Select(hex);
    }

    public void CancelPlacement()
    {
        _placeHex = null;
        RaisePlacement();
    }

    public async Task ConfirmPlacementAsync()
    {
        if (_placeHex is null || _client is null)
            return;
        if (string.IsNullOrWhiteSpace(PlacementName))
        {
            Status = "Donne un nom au stockpile.";
            return;
        }
        var hex = _placeHex;
        try
        {
            await _client.CreateAsync(new CreateStockpileRequest(
                PlacementName.Trim(), hex.Display, _placeTown, _placeType,
                PlacementUsesCode ? PlacementCode.Trim() : "", IsPublic: false,
                _placeRelX, _placeRelY));
            _placeHex = null;
            RaisePlacement();
            Status = $"Stockpile « {PlacementName.Trim()} » créé 📦";
            if (_stockpiles is not null)
                await _stockpiles.RefreshAsync(); // recharge la liste → le pin apparaît au refresh carte
            await RefreshAsync();
            Select(hex);
        }
        catch (Exception ex)
        {
            Status = $"Création impossible : {ex.Message}";
        }
    }

    private void RaisePlacement()
    {
        Raise(nameof(PlacementActive));
        Raise(nameof(PlacementTitle));
        Raise(nameof(PlacementInfo));
        Raise(nameof(PlacementName));
        Raise(nameof(PlacementCode));
        Raise(nameof(PlacementUsesCode));
    }

    private string NearestTownName(MapHexViewModel hex, double relX, double relY)
    {
        double px = hex.X + relX * hex.W, py = hex.Y + relY * hex.H;
        MapTownViewModel? best = null;
        double bd = double.MaxValue;
        foreach (var t in Towns)
        {
            if (t.Hex != hex)
                continue;
            double d = (t.X - px) * (t.X - px) + (t.Y - py) * (t.Y - py);
            if (d < bd) { bd = d; best = t; }
        }
        return best?.Name ?? "";
    }

    // ---------- Sélection ----------

    public void Select(MapHexViewModel? hex)
    {
        if (_selected is not null)
            _selected.IsSelected = false;
        _selected = hex;
        if (_selected is not null)
        {
            _selected.IsSelected = true;
            _ = LoadHiResTileAsync(_selected); // fond net au zoom
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedTowns.Clear();
        SelectedStructureSummary.Clear();
        SelectedStockpiles.Clear();
        SelectedRequests.Clear();
        RebuildOverlays();
        if (_selected is not null)
        {
            foreach (var t in Towns.Where(t => t.Hex == _selected).OrderBy(t => t.Name))
                SelectedTowns.Add(t);
            var structs = _selected.Structures.Select(s => new MapStructViewModel(_selected, s)).ToList();
            foreach (var g in structs.GroupBy(s => s.Label).OrderBy(g => g.Key))
                SelectedStructureSummary.Add($"{g.First().Glyph} {g.Key} ×{g.Count()}");
            if (_stockpiles is not null)
                foreach (var s in _stockpiles.Stockpiles.Where(s => FindHex(s.Hex) == _selected))
                    SelectedStockpiles.Add(s);
            if (_resupply is not null)
            {
                foreach (var r in _resupply.OpenRequests.Where(r => FindHex(r.Hex) == _selected))
                    SelectedRequests.Add($"🚚 {r.Title} — ouverte");
                foreach (var r in _resupply.TakenRequests.Where(r => FindHex(r.Hex) == _selected))
                    SelectedRequests.Add($"🚚 {r.Title} — {r.StatusLabel.ToLowerInvariant()}");
            }
        }
        Raise(nameof(SelectedName));
        Raise(nameof(HasSelection));
        Raise(nameof(NoSelection));
        Raise(nameof(SelectedHasStockpiles));
        Raise(nameof(SelectedHasRequests));
        Raise(nameof(SelectedHasStructures));
    }

    // ---------- Noms ----------

    private MapHexViewModel? FindHex(string hexFreeText)
    {
        if (string.IsNullOrWhiteSpace(hexFreeText))
            return null;
        string norm = Norm(hexFreeText);
        if (norm == "moors")
            norm = "mooringcounty"; // « The Moors » = MooringCountyHex
        return Hexes.FirstOrDefault(h => NormMap(h.Map) == norm)
            ?? Hexes.FirstOrDefault(h => NormMap(h.Map).StartsWith(norm) || norm.StartsWith(NormMap(h.Map)));
    }

    private static string DisplayNameFor(string map)
    {
        string norm = NormMap(map);
        foreach (var display in StockpileCatalog.Hexes)
        {
            string d = Norm(display);
            if (d == norm || (norm == "mooringcounty" && d == "moors"))
                return display;
        }
        // Repli : « KuuraStrandHex » → « Kuura Strand ».
        string raw = map.EndsWith("Hex") ? map[..^3] : map;
        var sb = new StringBuilder();
        foreach (char ch in raw)
        {
            if (char.IsUpper(ch) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string NormMap(string map) => Norm(map.EndsWith("Hex") ? map[..^3] : map);

    /// <summary>Même normalisation que le serveur : minuscules, sans accents/ponctuation, sans « the »/« of ».</summary>
    private static string Norm(string s)
    {
        string lowered = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lowered.Length);
        var word = new StringBuilder();
        void FlushWord()
        {
            string w = word.ToString();
            if (w.Length > 0 && w != "the" && w != "of")
                sb.Append(w);
            word.Clear();
        }
        foreach (char c in lowered)
        {
            if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                word.Append(c);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                FlushWord();
        }
        FlushWord();
        return sb.ToString();
    }
}
