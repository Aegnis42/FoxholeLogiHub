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

    /// <summary>Notifications Windows (toasts) : demandes d'amis, invitations, ravitaillement, stock critique.</summary>
    public bool NotificationsEnabled { get; set; } = true;

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
