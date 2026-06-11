using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FoxholeLogiHub.Api.War;

/// <summary>Infos de guerre brutes (endpoint /worldconquest/war).</summary>
public sealed record WarInfo(int WarNumber, string Winner, long ConquestStartTime, long? ConquestEndTime, int RequiredVictoryTowns);

/// <summary>
/// Une zone de région (un label « Major » de la carte — base officielle des Region Zones) avec
/// le contrôle de la base qui s'y trouve (Town Base tier 1-3, base relique ou fortin).
/// Tier = 1-3 pour une Town Base, 0 sinon (relique/fortin/zone sans base).
/// </summary>
public sealed record TownState(string Map, string Town, string NormTown, string TeamId, bool Scorched, bool VictoryBase, double X, double Y, int Tier);

/// <summary>Une structure logistique (dépôt, port, usine…) — IconType de l'API War.</summary>
public sealed record StructState(int IconType, string TeamId, double X, double Y);

/// <summary>Photographie de l'état de la guerre (rafraîchie périodiquement).</summary>
public sealed class WarSnapshot
{
    public required WarInfo Info { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    /// <summary>Villes par hexagone normalisé (clé = nom de carte API sans « Hex », normalisé).</summary>
    public required Dictionary<string, List<TownState>> TownsByHex { get; init; }
    /// <summary>Structures logistiques par nom de carte API (« DeadLandsHex »…).</summary>
    public required Dictionary<string, List<StructState>> StructuresByMap { get; init; }
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

    // Structures logistiques exposées sur la carte (iconTypes de l'API War).
    private static readonly HashSet<int> StructTypes = new()
    {
        11, // Hôpital
        12, // Usine de véhicules
        17, // Raffinerie
        18, // Chantier naval
        19, // Centre technologique
        27, // Fortin (Keep)
        33, // Dépôt de stockage
        34, // Usine
        39, // Chantier de construction
        45, 46, 47, // Bases reliques 1-3
        51, // Usine de production de masse (MPF)
        52, // Port
        88, // Dépôt d'aéronefs
        89, // Usine d'aéronefs
        91, 92, // Pistes d'aviation T1/T2
        // Champs et mines de ressources (planification des récoltes)
        20, // Champ de ferraille
        21, // Champ de composants
        22, // Champ de carburant
        23, // Champ de soufre
        32, // Mine de soufre
        38, // Mine de ferraille
        40, // Mine de composants
        61, // Champ de charbon
        62, // Champ de pétrole
        75, // Plateforme pétrolière
    };

    /// <summary>Bases qui définissent le contrôle d'une zone : Town Base 1-3, base relique 1-3, fortin.</summary>
    private static bool IsZoneBase(int icon) => icon is >= TownBaseT1 and <= TownBaseT3 or >= 45 and <= 47 or 27;

    private readonly WarApiClient _api;
    private readonly WarStateService _state;
    private readonly ILogger<WarRefreshService> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly Common.DiscordNotifier _discord;
    private readonly bool _disabled;

    // Stockpiles menacés au cycle précédent (clé stockpileId|raison) — pour ne notifier Discord
    // que les NOUVELLES menaces. Null tant qu'aucune base de comparaison (pas de spam au boot).
    private HashSet<string>? _lastThreats;

    // Labels statiques par carte — quasi immuables, on ne les recharge que si la guerre change.
    private readonly Dictionary<string, List<(string Text, double X, double Y)>> _labels = new();
    private int _labelsWar = -1;

    public WarRefreshService(WarApiClient api, WarStateService state, IConfiguration config,
        ILogger<WarRefreshService> logger, IServiceScopeFactory scopes, Common.DiscordNotifier discord)
    {
        _api = api;
        _state = state;
        _logger = logger;
        _scopes = scopes;
        _discord = discord;
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
        var structsByMap = new Dictionary<string, List<StructState>>();
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
                var structures = new List<StructState>();

                // 1) Chaque base de zone est assignée à son label « Major » le plus proche
                //    (priorité aux Town Bases sur les reliques/fortins si plusieurs bases par zone).
                var baseByLabel = new Dictionary<int, (int Icon, string Team, int Flags)>();
                foreach (var (iconType, teamId, x, y, flags) in dynamic)
                {
                    bool victory = (flags & FlagVictoryBase) != 0;
                    if (victory)
                    {
                        if (teamId == "WARDENS") wardens++;
                        else if (teamId == "COLONIALS") colonials++;
                    }
                    if (StructTypes.Contains(iconType))
                        structures.Add(new StructState(iconType, teamId, x, y));
                    if (!IsZoneBase(iconType) || labels.Count == 0)
                        continue;

                    int li = 0;
                    double best = double.MaxValue;
                    for (int i = 0; i < labels.Count; i++)
                    {
                        double d = (labels[i].X - x) * (labels[i].X - x) + (labels[i].Y - y) * (labels[i].Y - y);
                        if (d < best) { best = d; li = i; }
                    }
                    bool isTownBase = iconType is >= TownBaseT1 and <= TownBaseT3;
                    if (!baseByLabel.TryGetValue(li, out var existing)
                        || (isTownBase && existing.Icon is < TownBaseT1 or > TownBaseT3))
                        baseByLabel[li] = (iconType, teamId, flags);
                }

                // 2) Une zone par label « Major » (base officielle des Region Zones) — même sans base.
                var towns = new List<TownState>();
                for (int i = 0; i < labels.Count; i++)
                {
                    var (text, lx, ly) = labels[i];
                    if (string.IsNullOrEmpty(text))
                        continue;
                    baseByLabel.TryGetValue(i, out var b);
                    bool hasBase = b.Icon != 0;
                    int tier = hasBase && b.Icon is >= TownBaseT1 and <= TownBaseT3 ? b.Icon - TownBaseT1 + 1 : 0;
                    towns.Add(new TownState(map, text, WarStateService.Normalize(text),
                        hasBase ? b.Team : "NONE",
                        hasBase && (b.Flags & FlagScorched) != 0,
                        hasBase && (b.Flags & FlagVictoryBase) != 0,
                        lx, ly, tier));
                }

                string hexKey = WarStateService.Normalize(map.EndsWith("Hex") ? map[..^3] : map);
                townsByHex[hexKey] = towns;
                structsByMap[map] = structures;
            }
        }

        _state.Publish(new WarSnapshot
        {
            Info = war,
            FetchedAt = DateTimeOffset.UtcNow,
            TownsByHex = townsByHex,
            StructuresByMap = structsByMap,
            WardenVictoryTowns = wardens,
            ColonialVictoryTowns = colonials,
        });
        _logger.LogInformation("War API : guerre {War}, {Hexes} hexagones, {Towns} villes, VP W{W}/C{C}.",
            war.WarNumber, townsByHex.Count, townsByHex.Values.Sum(t => t.Count), wardens, colonials);

        try
        {
            await NotifyNewThreatsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Détection des menaces Discord échouée.");
        }
    }

    /// <summary>
    /// Compare l'état de menace des stockpiles (ville ennemie/rasée) avec le cycle précédent et
    /// prévient les régiments dont le webhook Discord est configuré — uniquement les NOUVELLES menaces.
    /// </summary>
    private async Task NotifyNewThreatsAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        var regs = await db.Regiments
            .Where(r => r.DiscordWebhookUrl != "")
            .ToListAsync();
        var current = new HashSet<string>(StringComparer.Ordinal);
        var newThreats = new Dictionary<string, List<string>>(); // webhook → lignes

        if (regs.Count > 0)
        {
            var regIds = regs.Select(r => r.Id).ToList();
            var stockpiles = await db.Stockpiles
                .Where(s => regIds.Contains(s.RegimentId) && !s.IsPublic)
                .ToListAsync();
            var regById = regs.ToDictionary(r => r.Id);

            foreach (var sp in stockpiles)
            {
                var reg = regById[sp.RegimentId];
                var town = _state.FindTown(sp.Hex, sp.Town);
                if (town is null)
                    continue;
                string control = WarStateService.ControlFor(town, reg.Faction);
                string? reason = town.Scorched ? "🔥 ville rasée"
                    : control == Contracts.WarTownControl.Enemy ? "⚠️ ville passée à l'ennemi"
                    : null;
                if (reason is null)
                    continue;
                string key = $"{sp.Id}|{reason}";
                current.Add(key);
                if (_lastThreats is not null && !_lastThreats.Contains(key))
                {
                    if (!newThreats.TryGetValue(reg.DiscordWebhookUrl, out var lines))
                        newThreats[reg.DiscordWebhookUrl] = lines = new List<string>();
                    string loc = Common.DiscordNotifier.Safe(sp.Hex) + (sp.Town.Length > 0 ? " · " + Common.DiscordNotifier.Safe(sp.Town) : "");
                    lines.Add($"• **{Common.DiscordNotifier.Safe(sp.Name)}** — {loc} : {reason}");
                }
            }
        }

        // Première passe après démarrage = base de comparaison, sans notifier le passif existant.
        bool baseline = _lastThreats is null;
        _lastThreats = current;
        if (baseline)
            return;

        foreach (var (url, lines) in newThreats)
            _discord.Send(url, "🚨 **Stockpiles menacés !**\n" + string.Join("\n", lines.Take(10))
                + (lines.Count > 10 ? $"\n… et {lines.Count - 10} autre(s)" : ""));
    }
}
