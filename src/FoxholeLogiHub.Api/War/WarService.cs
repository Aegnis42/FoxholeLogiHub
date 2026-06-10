using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FoxholeLogiHub.Api.War;

/// <summary>Infos de guerre brutes (endpoint /worldconquest/war).</summary>
public sealed record WarInfo(int WarNumber, string Winner, long ConquestStartTime, long? ConquestEndTime, int RequiredVictoryTowns);

/// <summary>Une ville (Town Base) avec son contrôle actuel.</summary>
public sealed record TownState(string Map, string Town, string NormTown, string TeamId, bool Scorched, bool VictoryBase);

/// <summary>Photographie de l'état de la guerre (rafraîchie périodiquement).</summary>
public sealed class WarSnapshot
{
    public required WarInfo Info { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    /// <summary>Villes par hexagone normalisé (clé = nom de carte API sans « Hex », normalisé).</summary>
    public required Dictionary<string, List<TownState>> TownsByHex { get; init; }
    public required int WardenVictoryTowns { get; init; }
    public required int ColonialVictoryTowns { get; init; }
}

/// <summary>
/// Client de l'API publique Foxhole (war-service-live) — lecture seule, légale, documentée.
/// On ne l'appelle JAMAIS depuis les requêtes utilisateur : seul le rafraîchissement périodique
/// la consulte, tout le monde lit le cache.
/// </summary>
public sealed class WarApiClient
{
    private const string Base = "https://war-service-live.foxholeservices.com/api/worldconquest";
    private readonly IHttpClientFactory _factory;

    public WarApiClient(IHttpClientFactory factory) => _factory = factory;

    private HttpClient Client()
    {
        var c = _factory.CreateClient("war-api");
        c.Timeout = TimeSpan.FromSeconds(15);
        return c;
    }

    public async Task<WarInfo> GetWarAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Base}/war", ct);
        var r = doc.RootElement;
        return new WarInfo(
            r.GetProperty("warNumber").GetInt32(),
            r.TryGetProperty("winner", out var w) ? w.GetString() ?? "NONE" : "NONE",
            r.GetProperty("conquestStartTime").GetInt64(),
            r.TryGetProperty("conquestEndTime", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : null,
            r.TryGetProperty("requiredVictoryTowns", out var v) ? v.GetInt32() : 32);
    }

    public async Task<List<string>> GetMapsAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Base}/maps", ct);
        return doc.RootElement.EnumerateArray().Select(m => m.GetString() ?? "").Where(m => m.Length > 0).ToList();
    }

    /// <summary>Labels « Major » (noms de villes) d'un hexagone, avec position relative.</summary>
    public async Task<List<(string Text, double X, double Y)>> GetMajorLabelsAsync(string map, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Base}/maps/{map}/static", ct);
        var list = new List<(string, double, double)>();
        if (!doc.RootElement.TryGetProperty("mapTextItems", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var i in items.EnumerateArray())
        {
            if (i.TryGetProperty("mapMarkerType", out var t) && t.GetString() == "Major"
                && i.TryGetProperty("text", out var txt))
                list.Add((txt.GetString() ?? "", i.GetProperty("x").GetDouble(), i.GetProperty("y").GetDouble()));
        }
        return list;
    }

    /// <summary>Items dynamiques (bases, dépôts…) d'un hexagone : type, équipe, position, flags.</summary>
    public async Task<List<(int IconType, string TeamId, double X, double Y, int Flags)>> GetDynamicItemsAsync(string map, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Base}/maps/{map}/dynamic/public", ct);
        var list = new List<(int, string, double, double, int)>();
        if (!doc.RootElement.TryGetProperty("mapItems", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var i in items.EnumerateArray())
            list.Add((
                i.GetProperty("iconType").GetInt32(),
                i.TryGetProperty("teamId", out var team) ? team.GetString() ?? "NONE" : "NONE",
                i.GetProperty("x").GetDouble(),
                i.GetProperty("y").GetDouble(),
                i.TryGetProperty("flags", out var f) ? f.GetInt32() : 0));
        return list;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var client = Client();
        using var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>
/// Cache de l'état de la guerre + résolution « hex/ville saisis par l'utilisateur » → ville API.
/// Les noms sont normalisés (minuscules, sans accents/ponctuation, sans « the »/« of ») pour
/// tolérer la saisie libre (« The Gallows », « Loch Mór », « The Moors »…).
/// </summary>
public sealed class WarStateService
{
    private volatile WarSnapshot? _current;

    // Cas où la normalisation ne suffit pas (nom d'affichage ≠ nom de carte API).
    private static readonly Dictionary<string, string> HexOverrides = new()
    {
        ["moors"] = "mooringcounty", // « The Moors » = MooringCountyHex
    };

    public WarSnapshot? Current => _current;

    public void Publish(WarSnapshot snapshot) => _current = snapshot;

    /// <summary>Retrouve l'état de la ville d'un stockpile (hex + ville en saisie libre). null si inconnue.</summary>
    public TownState? FindTown(string hexFreeText, string townFreeText)
    {
        var snap = _current;
        if (snap is null || string.IsNullOrWhiteSpace(hexFreeText) || string.IsNullOrWhiteSpace(townFreeText))
            return null;

        string hex = Normalize(hexFreeText);
        if (hex.Length == 0)
            return null;
        if (HexOverrides.TryGetValue(hex, out var mapped))
            hex = mapped;

        // Hex : égalité stricte sinon préfixe (« oarbreaker » ↔ « oarbreakerisles »).
        List<TownState>? towns = null;
        if (!snap.TownsByHex.TryGetValue(hex, out towns))
        {
            var key = snap.TownsByHex.Keys.FirstOrDefault(k => k.StartsWith(hex) || hex.StartsWith(k));
            if (key is null)
                return null;
            towns = snap.TownsByHex[key];
        }

        string town = Normalize(townFreeText);
        if (town.Length == 0)
            return null;
        return towns.FirstOrDefault(t => t.NormTown == town)
            ?? towns.FirstOrDefault(t => t.NormTown.StartsWith(town) || town.StartsWith(t.NormTown));
    }

    /// <summary>Contrôle relatif à une faction (« Wardens »/« Colonials » de notre base).</summary>
    public static string ControlFor(TownState? town, string faction)
    {
        if (town is null)
            return Contracts.WarTownControl.Unknown;
        string myTeam = faction.StartsWith("warden", StringComparison.OrdinalIgnoreCase) ? "WARDENS"
            : faction.StartsWith("colonial", StringComparison.OrdinalIgnoreCase) ? "COLONIALS"
            : "";
        if (myTeam.Length == 0)
            return Contracts.WarTownControl.Unknown;
        if (town.TeamId == "NONE")
            return Contracts.WarTownControl.Neutral;
        return town.TeamId == myTeam ? Contracts.WarTownControl.Friendly : Contracts.WarTownControl.Enemy;
    }

    /// <summary>minuscules + sans accents + alphanumérique, en ignorant les mots « the » et « of ».</summary>
    public static string Normalize(string s)
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

/// <summary>
/// Rafraîchit l'état de la guerre toutes les 5 minutes (1 appel war + 1 dynamique par hexagone ;
/// les labels statiques sont mis en cache pour toute la guerre). Désactivable via la config
/// <c>DisableWarApi=1</c> (tests d'intégration).
/// </summary>
public sealed class WarRefreshService : BackgroundService
{
    private const int TownBaseT1 = 56, TownBaseT3 = 58;        // Town Base tiers 1-3 (contrôle des villes)
    private const int FlagVictoryBase = 0x01, FlagScorched = 0x10;
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    private readonly WarApiClient _api;
    private readonly WarStateService _state;
    private readonly ILogger<WarRefreshService> _logger;
    private readonly bool _disabled;

    // Labels statiques par carte — quasi immuables, on ne les recharge que si la guerre change.
    private readonly Dictionary<string, List<(string Text, double X, double Y)>> _labels = new();
    private int _labelsWar = -1;

    public WarRefreshService(WarApiClient api, WarStateService state, IConfiguration config, ILogger<WarRefreshService> logger)
    {
        _api = api;
        _state = state;
        _logger = logger;
        _disabled = config["DisableWarApi"] == "1";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_disabled)
            return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Rafraîchissement War API échoué (réessai dans {Period}).", Period);
            }
            await Task.Delay(Period, ct);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        WarInfo war = await _api.GetWarAsync(ct);
        List<string> maps = await _api.GetMapsAsync(ct);

        if (_labelsWar != war.WarNumber)
        {
            _labels.Clear();
            _labelsWar = war.WarNumber;
        }

        var townsByHex = new Dictionary<string, List<TownState>>();
        int wardens = 0, colonials = 0;

        // Petites rafales pour rester poli avec l'API publique.
        foreach (var batch in maps.Chunk(8))
        {
            var tasks = batch.Select(async map =>
            {
                if (!_labels.TryGetValue(map, out var labels))
                {
                    labels = await _api.GetMajorLabelsAsync(map, ct);
                    lock (_labels) _labels[map] = labels;
                }
                var dynamic = await _api.GetDynamicItemsAsync(map, ct);
                return (map, labels, dynamic);
            }).ToList();

            foreach (var (map, labels, dynamic) in await Task.WhenAll(tasks))
            {
                var towns = new List<TownState>();
                foreach (var (iconType, teamId, x, y, flags) in dynamic)
                {
                    bool victory = (flags & FlagVictoryBase) != 0;
                    if (victory)
                    {
                        if (teamId == "WARDENS") wardens++;
                        else if (teamId == "COLONIALS") colonials++;
                    }
                    if (iconType is < TownBaseT1 or > TownBaseT3)
                        continue;

                    // La ville = le label « Major » le plus proche de la Town Base.
                    var nearest = labels
                        .OrderBy(l => (l.X - x) * (l.X - x) + (l.Y - y) * (l.Y - y))
                        .FirstOrDefault();
                    if (nearest.Text is null or "")
                        continue;
                    towns.Add(new TownState(map, nearest.Text, WarStateService.Normalize(nearest.Text),
                        teamId, (flags & FlagScorched) != 0, victory));
                }

                string hexKey = WarStateService.Normalize(map.EndsWith("Hex") ? map[..^3] : map);
                townsByHex[hexKey] = towns;
            }
        }

        _state.Publish(new WarSnapshot
        {
            Info = war,
            FetchedAt = DateTimeOffset.UtcNow,
            TownsByHex = townsByHex,
            WardenVictoryTowns = wardens,
            ColonialVictoryTowns = colonials,
        });
        _logger.LogInformation("War API : guerre {War}, {Hexes} hexagones, {Towns} villes, VP W{W}/C{C}.",
            war.WarNumber, townsByHex.Count, townsByHex.Values.Sum(t => t.Count), wardens, colonials);
    }
}
