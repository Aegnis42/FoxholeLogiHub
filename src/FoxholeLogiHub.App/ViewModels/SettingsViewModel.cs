using System.IO;
using FoxholeLogiHub.Core.Services;
using Microsoft.Win32;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Onglet Paramètres : tout ce qui se règle dans l'app, appliqué et sauvé immédiatement.
/// Chaque setter fait lecture → modification → sauvegarde pour ne jamais écraser les états
/// écrits ailleurs (positions d'overlay, etc.).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "FoxholeLogiHub";

    private readonly SettingsStore _store = new();
    private string _status = "";

    /// <summary>Les raccourcis F8/F9 ont changé : la fenêtre principale doit les ré-enregistrer.</summary>
    public event Action? HotkeysChanged;

    /// <summary>L'opacité de l'overlay a changé : appliquer aux fenêtres déjà ouvertes.</summary>
    public event Action? OverlayOpacityChanged;

    public string Status { get => _status; private set => Set(ref _status, value); }

    private void Mutate(Action<AppSettings> change, string? status = null)
    {
        var s = _store.Load();
        change(s);
        _store.Save(s);
        if (status is not null)
            Status = status;
    }

    // ---------- Général ----------

    public bool StartWithWindows
    {
        get => _store.Load().StartWithWindows;
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (value)
                    key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
                else
                    key.DeleteValue(RunValueName, throwOnMissingValue: false);
                Mutate(s => s.StartWithWindows = value,
                    value ? "L'app se lancera à l'ouverture de session." : "Démarrage automatique désactivé.");
            }
            catch (Exception ex)
            {
                Status = $"Registre inaccessible : {ex.Message}";
            }
            Raise();
        }
    }

    public bool CloseToTray
    {
        get => _store.Load().CloseToTray;
        set
        {
            Mutate(s => s.CloseToTray = value, value
                ? "Fermer la fenêtre laissera l'app en arrière-plan (icône de zone de notification)."
                : "Fermer la fenêtre quittera l'app.");
            Raise();
        }
    }

    public bool AutoCheckUpdates
    {
        get => _store.Load().AutoCheckUpdates;
        set { Mutate(s => s.AutoCheckUpdates = value); Raise(); }
    }

    // ---------- Notifications par catégorie ----------

    public bool NotifyFriendRequests
    {
        get => _store.Load().NotifyFriendRequests;
        set { Mutate(s => s.NotifyFriendRequests = value); Raise(); }
    }

    public bool NotifyRegimentInvites
    {
        get => _store.Load().NotifyRegimentInvites;
        set { Mutate(s => s.NotifyRegimentInvites = value); Raise(); }
    }

    public bool NotifyResupply
    {
        get => _store.Load().NotifyResupply;
        set { Mutate(s => s.NotifyResupply = value); Raise(); }
    }

    public bool NotifyCriticalStock
    {
        get => _store.Load().NotifyCriticalStock;
        set { Mutate(s => s.NotifyCriticalStock = value); Raise(); }
    }

    public bool NotifyMpfDone
    {
        get => _store.Load().NotifyMpfDone;
        set { Mutate(s => s.NotifyMpfDone = value); Raise(); }
    }

    // ---------- Overlay & raccourcis ----------

    public IReadOnlyList<string> HotkeyOptions { get; } =
        new[] { "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" };

    public string ImportHotkey
    {
        get => _store.Load().ImportHotkey;
        set
        {
            if (value == OverlayHotkey)
            {
                Status = "Ce raccourci est déjà utilisé par l'overlay.";
                Raise();
                return;
            }
            Mutate(s => s.ImportHotkey = value, $"Import en jeu : {value}.");
            HotkeysChanged?.Invoke();
            Raise();
        }
    }

    public string OverlayHotkey
    {
        get => _store.Load().OverlayHotkey;
        set
        {
            if (value == ImportHotkey)
            {
                Status = "Ce raccourci est déjà utilisé par l'import.";
                Raise();
                return;
            }
            Mutate(s => s.OverlayHotkey = value, $"Overlay : {value}.");
            HotkeysChanged?.Invoke();
            Raise();
        }
    }

    public double OverlayOpacity
    {
        get => _store.Load().OverlayOpacity;
        set
        {
            Mutate(s => s.OverlayOpacity = Math.Clamp(value, 0.5, 1.0));
            OverlayOpacityChanged?.Invoke();
            Raise();
            Raise(nameof(OverlayOpacityText));
        }
    }

    public string OverlayOpacityText => $"{_store.Load().OverlayOpacity:P0}";

    // ---------- Carte & données ----------

    /// <summary>Appliqué en direct à la carte par MainViewModel (câblage d'événement).</summary>
    public event Action<bool>? MapShowResourcesDefaultChanged;

    public bool MapShowResourcesDefault
    {
        get => _store.Load().MapShowResourcesDefault;
        set
        {
            Mutate(s => s.MapShowResourcesDefault = value);
            MapShowResourcesDefaultChanged?.Invoke(value);
            Raise();
        }
    }

    private static string TileCacheDir => Path.Combine(AppPaths.DataDirectory, "maptiles");

    public string TileCacheText
    {
        get
        {
            try
            {
                if (!Directory.Exists(TileCacheDir))
                    return "Cache des cartes : vide.";
                var files = Directory.GetFiles(TileCacheDir);
                double mb = files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
                return $"Cache des cartes : {files.Length} tuile(s), {mb:0.0} Mo.";
            }
            catch
            {
                return "Cache des cartes : taille inconnue.";
            }
        }
    }

    public void ClearTileCache()
    {
        try
        {
            if (Directory.Exists(TileCacheDir))
                foreach (var f in Directory.GetFiles(TileCacheDir))
                    File.Delete(f);
            Status = "Cache des cartes vidé — les tuiles se retéléchargeront à la prochaine ouverture.";
        }
        catch (Exception ex)
        {
            Status = $"Impossible de vider le cache : {ex.Message}";
        }
        Raise(nameof(TileCacheText));
    }

    public void OpenDataFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Status = $"Impossible d'ouvrir le dossier : {ex.Message}";
        }
    }

    // ---------- Serveur ----------

    private string? _serverUrlEdit;

    /// <summary>Valeur éditée (appliquée seulement au clic « Enregistrer »).</summary>
    public string ServerUrl
    {
        get => _serverUrlEdit ?? _store.Load().ApiBaseUrl;
        set => Set(ref _serverUrlEdit, value);
    }

    public void SaveServerUrl()
    {
        string url = ServerUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Status = "URL invalide (http(s)://…).";
            return;
        }
        Mutate(s => s.ApiBaseUrl = url, "Serveur enregistré — reconnecte-toi (onglet Amis) ou relance l'app.");
        _serverUrlEdit = null;
        Raise(nameof(ServerUrl));
    }

    public void ResetServerUrl()
    {
        _serverUrlEdit = new AppSettings().ApiBaseUrl; // défaut = serveur officiel
        Raise(nameof(ServerUrl));
        SaveServerUrl();
    }

    /// <summary>À l'ouverture de l'onglet : recalcule les valeurs dérivées (taille du cache…).</summary>
    public void RefreshComputed()
    {
        Raise(nameof(TileCacheText));
        Raise(nameof(StartWithWindows));
        Raise(nameof(CloseToTray));
        Raise(nameof(AutoCheckUpdates));
        Raise(nameof(OverlayOpacity));
        Raise(nameof(OverlayOpacityText));
    }
}
