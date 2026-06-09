using System.Text.Json;

namespace FoxholeLogiHub.Core.Services;

/// <summary>Réglages de l'application (URL du serveur d'amis, etc.).</summary>
public sealed class AppSettings
{
    /// <summary>URL de base de l'API d'amis. Localhost en dev ; URL Railway en prod.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";
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
