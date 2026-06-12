using System.Text.Json;

namespace FoxholeLogiHub.Core.Services;

/// <summary>Réglages de l'application (URL du serveur d'amis, etc.).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// URL de base de l'API. Par défaut : le serveur de production — indispensable pour les
    /// utilisateurs installés (premier lancement sans settings.json). En dev, écrire
    /// « http://localhost:5080 » dans %APPDATA%\FoxholeLogiHub\settings.json.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://foxhole-app-logistique-production.up.railway.app";

    /// <summary>Notifications Windows (toasts) : interrupteur principal.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    // Notifications par catégorie (sous l'interrupteur principal).
    public bool NotifyFriendRequests { get; set; } = true;
    public bool NotifyRegimentInvites { get; set; } = true;
    public bool NotifyResupply { get; set; } = true;
    public bool NotifyCriticalStock { get; set; } = true;
    public bool NotifyMpfDone { get; set; } = true;

    /// <summary>Lancer l'app à l'ouverture de session Windows (clé Run du registre).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Fermer la fenêtre = continuer en zone de notification (surveillance + overlay actifs).</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Recherche automatique des mises à jour au lancement.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Opacité des fenêtres d'overlay (0,5 à 1).</summary>
    public double OverlayOpacity { get; set; } = 1.0;

    /// <summary>Raccourcis globaux (touches F5 à F12).</summary>
    public string ImportHotkey { get; set; } = "F8";
    public string OverlayHotkey { get; set; } = "F9";

    /// <summary>Afficher les champs/mines de ressources sur la carte dès l'ouverture.</summary>
    public bool MapShowResourcesDefault { get; set; }

    /// <summary>
    /// Types d'icônes masqués sur la carte (iconType War API ; -1 = « autres structures »).
    /// Null = jamais touché → dérivé de <see cref="MapShowResourcesDefault"/>.
    /// </summary>
    public List<int>? MapHiddenIconTypes { get; set; }

    /// <summary>
    /// Panneaux de l'overlay (fenêtres toujours visibles) : état ouvert + position, par panneau
    /// ("hub", "stock", "resupply", "taken"). Le panneau « hub » sert aussi d'interrupteur global.
    /// </summary>
    public Dictionary<string, OverlayPanelState> OverlayPanels { get; set; } = new();

    public OverlayPanelState OverlayPanel(string key)
    {
        if (!OverlayPanels.TryGetValue(key, out var state))
            OverlayPanels[key] = state = new OverlayPanelState();
        return state;
    }
}

/// <summary>État persisté d'un panneau d'overlay.</summary>
public sealed class OverlayPanelState
{
    public bool Open { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
}

/// <summary>Persistance des réglages dans settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
}
