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
    public CompanionManager Companion { get; } = new();
    public ObservableCollection<Loadout> Loadouts { get; } = new();

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

    public MainViewModel()
    {
        _notifier.Enabled = _settingsStore.Load().NotificationsEnabled;

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

    public void ShowDashboard() => SetTab("Dashboard");
    public void ShowProfile() => SetTab("Profil");
    public void ShowFriends() => SetTab("Amis");
    public void ShowRegiment() => SetTab("Régiment");
    public void ShowStockpiles() => SetTab("Stockpiles");
    public void ShowResupply() => SetTab("Ravitaillement");
    public void ShowTaken() => SetTab("Prises");
    public void ShowMap() => SetTab("Carte");

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
                // Notifications Windows (toasts) — activables dans Profil.
                Friends.ToastRequested += _notifier.Show;
                Resupply.ToastRequested += _notifier.Show;
                Stockpiles.ToastRequested += _notifier.Show;
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
