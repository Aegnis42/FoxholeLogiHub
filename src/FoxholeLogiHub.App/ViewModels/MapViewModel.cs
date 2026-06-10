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
    public double StrokeThickness => IsSelected ? 2.5 : 1.5;

    public void RaiseFill() => Raise(nameof(Fill));
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
    };

    public MapStructViewModel(MapHexViewModel hex, WarMapStructDto s)
    {
        Hex = hex;
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
    public string Glyph { get; }
    public string Label { get; }
    public double X { get; }
    public double Y { get; }
    public Brush TeamBrush { get; }
    public string Tooltip { get; }
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

/// <summary>Un pin « stockpile » sur la carte.</summary>
public sealed class MapPinViewModel
{
    public MapPinViewModel(StockpileItemViewModel s, MapHexViewModel hex, double x, double y)
    {
        Stockpile = s;
        Hex = hex;
        X = x; Y = y;
    }

    public StockpileItemViewModel Stockpile { get; }
    public MapHexViewModel Hex { get; }
    public double X { get; }
    public double Y { get; }
    public bool IsThreatened => Stockpile.IsThreatened;
    public string Tooltip => $"{Stockpile.Name} ({Stockpile.TypeLabel}) — {Stockpile.LocationLabel}"
        + (Stockpile.IsThreatened ? $"\n{Stockpile.ThreatLabel}" : "");
}

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
    private MapHexViewModel? _selected;

    public ObservableCollection<MapHexViewModel> Hexes { get; } = new();
    public ObservableCollection<MapTownViewModel> Towns { get; } = new();
    public ObservableCollection<MapPinViewModel> Pins { get; } = new();

    public ObservableCollection<MapTownViewModel> SelectedTowns { get; } = new();
    public ObservableCollection<MapTownViewModel> SelectedTownLabels { get; } = new();
    public ObservableCollection<MapStructViewModel> SelectedStructures { get; } = new();
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
    /// lancement, puis tout vient du cache disque) et les applique en vignettes 256 px.
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
                var thumb = MapTileService.LoadImage(path, 256);
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

            // Ancrage : la ville correspondante si on la connaît, sinon le centre (en éventail).
            double x, y;
            var town = Towns.FirstOrDefault(t => t.Hex == hex && Norm(t.Name) == Norm(s.Town));
            if (town is not null)
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
        SelectedTownLabels.Clear();
        SelectedStructures.Clear();
        SelectedStructureSummary.Clear();
        SelectedStockpiles.Clear();
        SelectedRequests.Clear();
        if (_selected is not null)
        {
            foreach (var t in Towns.Where(t => t.Hex == _selected).OrderBy(t => t.Name))
            {
                SelectedTowns.Add(t);
                SelectedTownLabels.Add(t);
            }
            foreach (var s in _selected.Structures)
                SelectedStructures.Add(new MapStructViewModel(_selected, s));
            foreach (var g in SelectedStructures.GroupBy(s => s.Label).OrderBy(g => g.Key))
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
