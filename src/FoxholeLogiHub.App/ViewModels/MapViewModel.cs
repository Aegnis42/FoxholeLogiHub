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
    }

    public string Map { get; }
    public string Display { get; }
    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }
    public PointCollection Points { get; }

    public List<WarMapTownDto> Towns { get; } = new();
    public Brush Fill { get; set; } = Palette.MapNeutral;

    public bool IsSelected { get => _isSelected; set { Set(ref _isSelected, value); Raise(nameof(Stroke)); Raise(nameof(StrokeThickness)); } }
    public Brush Stroke => IsSelected ? Brushes.White : Palette.MapStroke;
    public double StrokeThickness => IsSelected ? 2.5 : 1.5;

    public void RaiseFill() => Raise(nameof(Fill));
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
        const double r = 4;
        X = hex.X + t.X * hex.W - r;
        Y = hex.Y + t.Y * hex.H - r;
        Fill = t.Scorched ? Palette.Critical
            : t.Team == "WARDENS" ? Palette.Wardens
            : t.Team == "COLONIALS" ? Palette.Colonials
            : Palette.MapTownNeutral;
    }

    public MapHexViewModel Hex { get; }
    public string Name { get; }
    public string Team { get; }
    public bool Scorched { get; }
    public double X { get; }
    public double Y { get; }
    public Brush Fill { get; }

    public string TeamLabel => Scorched ? "rasée 🔥" : Team switch
    {
        "WARDENS" => "Wardens",
        "COLONIALS" => "Colonials",
        _ => "neutre",
    };
    public string Tooltip => $"{Name} — {TeamLabel}";
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
    private StockpileClient? _client;
    private string _clientKey = "";
    private StockpilesViewModel? _stockpiles;
    private ResupplyViewModel? _resupply;

    private bool _authed;
    private string _status = "";
    private MapHexViewModel? _selected;

    public ObservableCollection<MapHexViewModel> Hexes { get; } = new();
    public ObservableCollection<MapTownViewModel> Towns { get; } = new();
    public ObservableCollection<MapPinViewModel> Pins { get; } = new();

    public ObservableCollection<MapTownViewModel> SelectedTowns { get; } = new();
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
            Status = map is { Available: true }
                ? $"{Towns.Count} villes · {Pins.Count} stockpile(s) · molette = zoom, glisser = déplacer, clic = détail"
                : "Données de guerre pas encore disponibles (réessaie dans quelques minutes).";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
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
            hex.Fill = Palette.MapNeutral;
        }
        if (map is not { Available: true })
        {
            foreach (var hex in Hexes)
                hex.RaiseFill();
            return;
        }

        var byMap = Hexes.ToDictionary(h => h.Map, StringComparer.OrdinalIgnoreCase);
        foreach (var h in map.Hexes)
        {
            if (!byMap.TryGetValue(h.Map, out var hex))
                continue;
            hex.Towns.AddRange(h.Towns);
            int w = h.Towns.Count(t => t.Team == "WARDENS");
            int c = h.Towns.Count(t => t.Team == "COLONIALS");
            hex.Fill = w > 0 && c > 0 ? Palette.MapContested
                : w > 0 ? Palette.MapWarden
                : c > 0 ? Palette.MapColonial
                : Palette.MapNeutral;
            foreach (var t in h.Towns)
                Towns.Add(new MapTownViewModel(hex, t));
        }
        foreach (var hex in Hexes)
            hex.RaiseFill();
    }

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

            // Position : la ville correspondante si on la connaît, sinon le centre (en éventail).
            double x, y;
            var town = Towns.FirstOrDefault(t => t.Hex == hex && Norm(t.Name) == Norm(s.Town));
            if (town is not null)
            {
                x = town.X - 6;
                y = town.Y - 22;
            }
            else
            {
                int n = perHex.GetValueOrDefault(hex.Map);
                x = hex.X + hex.W / 2 - 9 + (n % 3) * 16 - 16;
                y = hex.Y + hex.H / 2 + (n / 3) * 18 - 6;
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
            _selected.IsSelected = true;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedTowns.Clear();
        SelectedStockpiles.Clear();
        SelectedRequests.Clear();
        if (_selected is not null)
        {
            foreach (var t in Towns.Where(t => t.Hex == _selected).OrderBy(t => t.Name))
                SelectedTowns.Add(t);
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
