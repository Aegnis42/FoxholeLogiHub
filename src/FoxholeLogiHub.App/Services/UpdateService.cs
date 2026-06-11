using Velopack;
using Velopack.Sources;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Mises à jour automatiques via les GitHub Releases du dépôt (Velopack).
/// Ne fait rien en exécution « lâche » (dev) : seule l'app installée par Setup.exe se met à jour.
/// </summary>
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/Aegnis42/foxhole-app--logistique";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));
    private UpdateInfo? _pending;

    /// <summary>Version installée (ex. « 1.0.0 ») ou celle de l'assembly en dev.</summary>
    public string CurrentVersion =>
        _manager.IsInstalled && _manager.CurrentVersion is not null
            ? _manager.CurrentVersion.ToString()
            : typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "dev";

    /// <summary>
    /// Cherche puis télécharge une mise à jour. Renvoie la version prête à installer, ou null
    /// (pas de mise à jour, app non installée, ou réseau indisponible — jamais d'exception).
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync()
    {
        try
        {
            if (!_manager.IsInstalled)
                return null;
            _pending = await _manager.CheckForUpdatesAsync();
            if (_pending is null)
                return null;
            await _manager.DownloadUpdatesAsync(_pending);
            return _pending.TargetFullRelease.Version.ToString();
        }
        catch
        {
            return null; // la mise à jour ne doit jamais gêner l'app
        }
    }

    /// <summary>Applique la mise à jour téléchargée et redémarre l'application.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending);
    }
}
