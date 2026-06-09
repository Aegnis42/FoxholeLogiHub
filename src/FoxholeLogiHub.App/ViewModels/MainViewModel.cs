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
    private Account? _account;

    private string _status = "";
    private bool _hasData;
    private string _activeTab = "Profil";
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
    public CompanionManager Companion { get; } = new();
    public ObservableCollection<Loadout> Loadouts { get; } = new();

    public MainViewModel()
    {
        // Le module amis porte la connexion temps réel ; on relaie aux modules régiment/stockpiles.
        Friends.Authenticated += () => _ = RefreshSocialAsync();
        Friends.LoggedOut += () => { Regiment.ClearAuth(); Stockpiles.ClearAuth(); };
        Friends.RegimentChanged += () => _ = RefreshSocialAsync();
        Friends.RegimentInviteReceived += () => _ = Regiment.ReloadInvitesAsync();
        Friends.StockpilesChanged += () => _ = Stockpiles.RefreshAsync();
    }

    // Régiment d'abord (ses alliances servent au partage des stockpiles), puis stockpiles.
    private async Task RefreshSocialAsync()
    {
        await Regiment.RefreshAsync();
        await Stockpiles.RefreshAsync();
    }

    // --- Navigation ---
    public bool IsProfileActive => _activeTab == "Profil";
    public bool IsFriendsActive => _activeTab == "Amis";
    public bool IsRegimentActive => _activeTab == "Régiment";
    public bool IsStockpilesActive => _activeTab == "Stockpiles";

    public void ShowProfile() => SetTab("Profil");
    public void ShowFriends() => SetTab("Amis");
    public void ShowRegiment() => SetTab("Régiment");
    public void ShowStockpiles() => SetTab("Stockpiles");

    private void SetTab(string tab)
    {
        if (_activeTab == tab)
            return;
        _activeTab = tab;
        Raise(nameof(IsProfileActive));
        Raise(nameof(IsFriendsActive));
        Raise(nameof(IsRegimentActive));
        Raise(nameof(IsStockpilesActive));
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
                FactionId.Wardens => new SolidColorBrush(Color.FromRgb(0x24, 0x5C, 0x8A)),
                FactionId.Colonials => new SolidColorBrush(Color.FromRgb(0x51, 0x6C, 0x42)),
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
