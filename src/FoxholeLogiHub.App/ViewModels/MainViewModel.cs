using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;
using FactionId = FoxholeLogiHub.Core.Models.Faction;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Shell de l'application : profil + amis + navigation.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AccountService _accountService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly Notifier _notifier = new();
    private readonly UpdateService _updater = new();
    private Account? _account;
    private bool _mapPickWired;

    private string _status = "";
    private bool _hasData;
    private string _activeTab = "Dashboard";
    private string _steamId = "";
    private string _displayName = "";
    private string _personaName = "";
    private ImageSource? _avatar;
    private string _faction = "";
    private Brush _factionBrush = Brushes.DimGray;
    private string _server = "";
    private string _language = "";
    private int _warCount;

    public FriendsViewModel Friends { get; } = new();
    public RegimentViewModel Regiment { get; } = new();
    public StockpilesViewModel Stockpiles { get; } = new();
    public ResupplyViewModel Resupply { get; } = new();
    public MapViewModel Map { get; } = new();
    public CalculatorViewModel Calculator { get; } = new();
    public SettingsViewModel Settings { get; } = new();
    public CompanionManager Companion { get; } = new();
    public ObservableCollection<Loadout> Loadouts { get; } = new();

    /// <summary>Icône de zone de notification (la fenêtre s'y abonne pour restaurer/quitter).</summary>
    public Services.Notifier Notifier => _notifier;

    /// <summary>Notifications Windows activées (persisté dans settings.json).</summary>
    public bool NotificationsEnabled
    {
        get => _notifier.Enabled;
        set
        {
            if (_notifier.Enabled == value)
                return;
            _notifier.Enabled = value;
            var s = _settingsStore.Load();
            s.NotificationsEnabled = value;
            _settingsStore.Save(s);
            Raise();
        }
    }

    /// <summary>À appeler à la fermeture de la fenêtre (retire l'icône de zone de notification).</summary>
    public void Shutdown() => _notifier.Dispose();

    // ---------- Mises à jour (Velopack / GitHub Releases) ----------

    private string? _updateVersion;

    public string AppVersion => $"v{_updater.CurrentVersion}";
    public bool UpdateReady => _updateVersion is not null;
    public string UpdateLabel => _updateVersion is null ? "" : $"🔄 Mise à jour v{_updateVersion} — Redémarrer";

    /// <summary>Applique la mise à jour téléchargée et relance l'app.</summary>
    public void ApplyUpdate() => _updater.ApplyAndRestart();

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10)); // laisse l'app démarrer tranquillement
        string? version = await _updater.CheckAndDownloadAsync();
        if (version is null)
            return;
        _updateVersion = version;
        Raise(nameof(UpdateReady));
        Raise(nameof(UpdateLabel));
        _notifier.Show("Mise à jour prête", $"FoxholeLogiHub v{version} est téléchargée — clique « Redémarrer » dans l'app.");
    }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    /// <summary>Vérification manuelle (bouton des Paramètres), indépendante du réglage auto.</summary>
    public async Task CheckUpdatesNowAsync()
    {
        UpdateStatus = "Recherche de mise à jour…";
        try
        {
            string? version = await _updater.CheckAndDownloadAsync();
            if (version is null)
            {
                UpdateStatus = UpdateReady
                    ? $"v{_updateVersion} déjà téléchargée — clique « Redémarrer » (barre latérale)."
                    : $"À jour ({AppVersion}).";
                return;
            }
            _updateVersion = version;
            Raise(nameof(UpdateReady));
            Raise(nameof(UpdateLabel));
            UpdateStatus = $"v{version} téléchargée — clique « Redémarrer » (barre latérale).";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Vérification impossible : {ex.Message}";
        }
    }

    public MainViewModel()
    {
        var bootSettings = _settingsStore.Load();
        _notifier.Enabled = bootSettings.NotificationsEnabled;
        Map.ShowResources = bootSettings.MapShowResourcesDefault;
        if (bootSettings.AutoCheckUpdates)
            _ = CheckForUpdateAsync();

        // Réglage carte appliqué en direct depuis l'onglet Paramètres.
        Settings.MapShowResourcesDefaultChanged += show => Map.ShowResources = show;

        // Le module amis porte la connexion temps réel ; on relaie aux modules régiment/stockpiles/ravito.
        Friends.Authenticated += () => _ = RefreshSocialAsync();
        Friends.LoggedOut += () => { Regiment.ClearAuth(); Stockpiles.ClearAuth(); Resupply.ClearAuth(); Map.ClearAuth(); };
        Friends.RegimentChanged += () => _ = RefreshSocialAsync();
        Friends.RegimentInviteReceived += () => _ = Regiment.ReloadInvitesAsync();
        // Les stockpiles changent → mettre à jour aussi les manques du ravitaillement.
        Friends.StockpilesChanged += () => _ = RefreshStockAndResupplyAsync();
        Friends.ResupplyChanged += () => _ = Resupply.RefreshAsync();
    }

    // Régiment d'abord (ses alliances servent au partage des stockpiles), puis stockpiles, puis ravito,
    // puis la carte (qui lit les stockpiles + demandes déjà chargés).
    private async Task RefreshSocialAsync()
    {
        await Regiment.RefreshAsync();
        await Stockpiles.RefreshAsync();
        await Resupply.RefreshAsync();
        await Map.RefreshAsync();
    }

    private async Task RefreshStockAndResupplyAsync()
    {
        await Stockpiles.RefreshAsync();
        await Resupply.RefreshAsync();
        await Map.RefreshAsync();
    }

    /// <summary>Fin de guerre : archive locale (JSON) puis purge serveur, puis rafraîchit tout.</summary>
    public async Task<string?> WarResetAsync()
    {
        string? archive = null;
        try { archive = await Stockpiles.ExportWarArchiveAsync(Resupply); }
        catch { /* la purge ne doit pas être bloquée par l'archive */ }
        await Regiment.WarResetAsync();
        await RefreshStockAndResupplyAsync();
        return archive;
    }

    // --- Navigation ---
    public bool IsDashboardActive => _activeTab == "Dashboard";
    public bool IsProfileActive => _activeTab == "Profil";
    public bool IsFriendsActive => _activeTab == "Amis";
    public bool IsRegimentActive => _activeTab == "Régiment";
    public bool IsStockpilesActive => _activeTab == "Stockpiles";
    public bool IsResupplyActive => _activeTab == "Ravitaillement";
    public bool IsTakenActive => _activeTab == "Prises";
    public bool IsMapActive => _activeTab == "Carte";
    public bool IsCalculatorActive => _activeTab == "Calculatrice";
    public bool IsSettingsActive => _activeTab == "Paramètres";

    public void ShowDashboard() => SetTab("Dashboard");
    public void ShowProfile() => SetTab("Profil");
    public void ShowFriends() => SetTab("Amis");
    public void ShowRegiment() => SetTab("Régiment");
    public void ShowStockpiles() => SetTab("Stockpiles");
    public void ShowResupply() => SetTab("Ravitaillement");
    public void ShowTaken() => SetTab("Prises");
    public void ShowMap() => SetTab("Carte");
    public void ShowCalculator() => SetTab("Calculatrice");

    public void ShowSettings()
    {
        Settings.RefreshComputed(); // taille du cache, états registre…
        SetTab("Paramètres");
    }

    private void SetTab(string tab)
    {
        if (_activeTab == tab)
            return;
        _activeTab = tab;
        Raise(nameof(IsDashboardActive));
        Raise(nameof(IsProfileActive));
        Raise(nameof(IsFriendsActive));
        Raise(nameof(IsRegimentActive));
        Raise(nameof(IsStockpilesActive));
        Raise(nameof(IsResupplyActive));
        Raise(nameof(IsTakenActive));
        Raise(nameof(IsMapActive));
        Raise(nameof(IsCalculatorActive));
        Raise(nameof(IsSettingsActive));
    }

    // --- Profil ---
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
    public string SteamId { get => _steamId; private set => Set(ref _steamId, value); }
    public string PersonaName { get => _personaName; private set => Set(ref _personaName, value); }
    public ImageSource? Avatar { get => _avatar; private set => Set(ref _avatar, value); }
    public string Faction { get => _faction; private set => Set(ref _faction, value); }
    public Brush FactionBrush { get => _factionBrush; private set => Set(ref _factionBrush, value); }
    public string Server { get => _server; private set => Set(ref _server, value); }
    public string Language { get => _language; private set => Set(ref _language, value); }
    public int WarCount { get => _warCount; private set => Set(ref _warCount, value); }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public void Load()
    {
        try
        {
            AccountResult result = _accountService.LoadOrCreate();
            if (result.Error is not null || result.Account is null)
            {
                HasData = false;
                Status = result.Error ?? "Compte indisponible.";
                return;
            }

            _account = result.Account;

            SteamId = _account.SteamId;
            DisplayName = _account.DisplayName;
            PersonaName = result.Profile?.PersonaName ?? "";
            Avatar = LoadImage(_account.AvatarPath);

            Faction = _account.Faction.ToString();
            FactionBrush = _account.Faction switch
            {
                FactionId.Wardens => Palette.Wardens,
                FactionId.Colonials => Palette.Colonials,
                _ => Brushes.DimGray,
            };

            PlayerSave? save = result.Save;
            Server = save?.LastServer ?? "—";
            Language = save?.Language ?? "—";
            WarCount = save?.WarsJoined.Count ?? 0;

            Loadouts.Clear();
            foreach (Loadout lo in save?.Loadouts ?? Enumerable.Empty<Loadout>())
                Loadouts.Add(lo);

            HasData = true;
            Status = $"Connecté en tant que {DisplayName} ({Faction}).";

            // Démarre la connexion au serveur d'amis (présence temps réel) ; régiment+stockpiles suivent.
            Regiment.Initialize(_account, Friends);
            Stockpiles.Initialize(Regiment, Companion);
            Resupply.Initialize(Regiment, Stockpiles);
            Map.Initialize(Stockpiles, Resupply);

            // Création hors port/dépôt depuis l'onglet Stockpiles → placement à la main sur la carte.
            if (!_mapPickWired)
            {
                _mapPickWired = true;
                Stockpiles.PlaceOnMapRequested += pick =>
                {
                    ShowMap();
                    Map.BeginPickPosition(pick);
                };
                Map.PickCompleted += () => Stockpiles.OnPlacedExternally();
                // « Où produire ? » : la demande prise en charge ouvre la carte avec les lieux du plan.
                Resupply.ProduceOnMapRequested += (hex, icons) =>
                {
                    ShowMap();
                    Map.HighlightProduction(hex, icons);
                };
                // Notifications Windows (toasts), filtrées par catégorie (onglet Paramètres).
                // Le routage se fait sur les titres — des constantes à nous (voir les VMs émetteurs).
                Friends.ToastRequested += (title, msg) =>
                {
                    var s = _settingsStore.Load();
                    bool isRegiment = title.Contains("régiment", StringComparison.OrdinalIgnoreCase);
                    if (isRegiment ? s.NotifyRegimentInvites : s.NotifyFriendRequests)
                        _notifier.Show(title, msg);
                };
                Resupply.ToastRequested += (title, msg) =>
                {
                    if (_settingsStore.Load().NotifyResupply)
                        _notifier.Show(title, msg);
                };
                Stockpiles.ToastRequested += (title, msg) =>
                {
                    var s = _settingsStore.Load();
                    bool isMpf = title.StartsWith("MPF", StringComparison.OrdinalIgnoreCase);
                    if (isMpf ? s.NotifyMpfDone : s.NotifyCriticalStock)
                        _notifier.Show(title, msg);
                };
                // Calculatrice → brouillon de demande de ravitaillement.
                Calculator.SendToResupplyRequested += items =>
                {
                    Resupply.ImportDraft(items);
                    ShowResupply();
                };
            }

            Companion.EnsureStarted();
            _ = Friends.InitializeAsync(_account);
        }
        catch (Exception ex)
        {
            HasData = false;
            Status = $"Erreur : {ex.Message}";
        }
    }

    public void SaveProfile()
    {
        if (_account is null)
            return;

        _accountService.UpdateDisplayName(_account, DisplayName);
        DisplayName = _account.DisplayName;
        Status = "Profil enregistré.";
    }

    private static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // ne verrouille pas le fichier
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(Path.GetFullPath(path));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
