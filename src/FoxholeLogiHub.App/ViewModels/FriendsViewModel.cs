using System.Collections.ObjectModel;
using System.Windows;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Gère l'authentification Steam, la liste d'amis, les demandes reçues et la présence temps réel.
/// </summary>
public sealed class FriendsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();
    private readonly SteamAuthService _auth = new();
    private readonly AppSettings _settings;

    private Account? _account;
    private FriendsClient? _client;
    private string? _token;

    private string _apiBaseUrl;
    private string _myFriendCode = "—";
    private string _addCodeInput = "";
    private string _status = "Non connecté.";
    private bool _connected;
    private bool _busy;
    private bool _needsLogin = true;

    public FriendsViewModel()
    {
        _settings = _settingsStore.Load();
        _apiBaseUrl = _settings.ApiBaseUrl;
        Friends.CollectionChanged += (_, _) => { Raise(nameof(HasNoFriends)); Raise(nameof(OnlineSummary)); };
        Requests.CollectionChanged += (_, _) => Raise(nameof(HasRequests));
    }

    /// <summary>« — 2/5 en ligne » pour l'en-tête du panneau amis ("" sans amis).</summary>
    public string OnlineSummary => Friends.Count == 0
        ? ""
        : $"— {Friends.Count(f => f.Online)}/{Friends.Count} en ligne";

    /// <summary>À appeler quand une présence change (le compte en ligne de l'en-tête dépend des items).</summary>
    private void RaiseOnlineSummary() => Raise(nameof(OnlineSummary));

    /// <summary>Événements relayés au shell (pour le ViewModel régiment notamment).</summary>
    public event Action? Authenticated;
    public event Action? LoggedOut;
    public event Action? RegimentChanged;
    public event Action? RegimentInviteReceived;
    public event Action? StockpilesChanged;
    public event Action? ResupplyChanged;

    public ObservableCollection<FriendItemViewModel> Friends { get; } = new();
    public ObservableCollection<FriendRequestItemViewModel> Requests { get; } = new();

    public bool HasNoFriends => Friends.Count == 0;
    public bool HasRequests => Requests.Count > 0;

    public string MyFriendCode { get => _myFriendCode; private set => Set(ref _myFriendCode, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Connected { get => _connected; private set => Set(ref _connected, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public bool NeedsLogin
    {
        get => _needsLogin;
        private set { Set(ref _needsLogin, value); Raise(nameof(IsLoggedIn)); }
    }
    public bool IsLoggedIn => !_needsLogin;

    public string AddCodeInput { get => _addCodeInput; set => Set(ref _addCodeInput, value); }
    public string ApiBaseUrl { get => _apiBaseUrl; set => Set(ref _apiBaseUrl, value); }

    public async Task InitializeAsync(Account account)
    {
        _account = account;
        _token = _tokenStore.Load();
        if (_token is null)
        {
            NeedsLogin = true;
            Status = "Connecte-toi avec Steam pour activer les amis.";
            return;
        }
        await ConnectAsync();
    }

    /// <summary>Lance « Sign in through Steam » dans le navigateur, puis se connecte.</summary>
    public async Task LoginWithSteamAsync()
    {
        if (Busy)
            return;

        Busy = true;
        Status = "Connexion Steam : valide dans le navigateur qui vient de s'ouvrir…";
        string? token;
        try
        {
            token = await _auth.LoginAsync(ApiBaseUrl.Trim());
        }
        catch (Exception ex)
        {
            Status = $"Échec de la connexion Steam : {ex.Message}";
            Busy = false;
            return;
        }
        Busy = false;

        if (token is null)
        {
            Status = "Connexion Steam annulée ou expirée.";
            return;
        }

        _token = token;
        _tokenStore.Save(token);
        NeedsLogin = false;
        await ConnectAsync();
    }

    public async Task LogoutAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        _token = null;
        _tokenStore.Clear();
        Friends.Clear();
        Requests.Clear();
        MyFriendCode = "—";
        Connected = false;
        NeedsLogin = true;
        Status = "Déconnecté.";
        LoggedOut?.Invoke();
    }

    public async Task ConnectAsync()
    {
        if (_account is null || _token is null || Busy)
            return;

        Busy = true;
        Connected = false;
        Status = "Connexion au serveur…";

        try
        {
            _settings.ApiBaseUrl = ApiBaseUrl.Trim();
            _settingsStore.Save(_settings);

            if (_client is not null)
                await _client.DisposeAsync();
            _client = new FriendsClient(_settings.ApiBaseUrl, _token, () => _tokenStore.Load());
            _client.HubReconnected += () => OnUi(() => { Connected = true; Status = $"Reconnecté · {Friends.Count} ami(s)."; });
            _client.HubClosed += () => OnUi(() =>
            {
                Connected = false;
                Status = "Connexion temps réel perdue — vérifie ta connexion puis relance l'app si besoin.";
            });

            UserDto me = await _client.UpsertUserAsync(_account.DisplayName, _account.Faction.ToString());
            MyFriendCode = Format(me.FriendCode);

            // Démarrage plus rapide : l'avatar part en tâche de fond (purement cosmétique) et
            // amis + demandes se chargent en parallèle (2 allers-retours → 1).
            var client = _client;
            _ = Task.Run(async () => { try { await client.UploadAvatarAsync(_account.AvatarPath ?? ""); } catch { } });
            await Task.WhenAll(ReloadFriendsAsync(), ReloadRequestsAsync());

            await _client.ConnectPresenceAsync(new PresenceHandlers(
                OnPresenceChanged, OnOnlineFriends, OnFriendRequestReceived, OnFriendsChanged,
                () => OnUi(() => RegimentChanged?.Invoke()),
                () => OnUi(() =>
                {
                    RegimentInviteReceived?.Invoke();
                    ToastRequested?.Invoke("Invitation de régiment", "Tu as été invité dans un régiment.");
                }),
                () => OnUi(() => StockpilesChanged?.Invoke()),
                () => OnUi(() => ResupplyChanged?.Invoke())));

            Connected = true;
            NeedsLogin = false;
            Status = $"Connecté · {Friends.Count} ami(s).";
            Authenticated?.Invoke();
        }
        catch (AuthRequiredException ex)
        {
            _token = null;
            _tokenStore.Clear();
            Connected = false;
            NeedsLogin = true;
            Status = "Session expirée — reconnecte-toi avec Steam."
                + (ex.Message.Length > 0 ? $" ({ex.Message})" : "");
        }
        catch (Exception ex)
        {
            Connected = false;
            Status = $"Hors ligne : {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task SendRequestAsync()
    {
        if (_client is null)
        {
            Status = "Pas encore connecté.";
            return;
        }
        if (Busy)
            return;

        string code = AddCodeInput.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return;

        Busy = true;
        try
        {
            SendFriendRequestResult result = await _client.SendRequestAsync(code);
            AddCodeInput = "";
            if (result.Accepted)
            {
                await ReloadFriendsAsync();
                Status = $"{result.DisplayName} ajouté (demande croisée).";
            }
            else
            {
                Status = $"Demande envoyée à {result.DisplayName}.";
            }
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { await OnAuthLostAsync(); }
        catch (Exception ex) { Status = $"Échec : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task AcceptRequestAsync(FriendRequestItemViewModel request) => await RespondAsync(request, true);
    public async Task DeclineRequestAsync(FriendRequestItemViewModel request) => await RespondAsync(request, false);

    private async Task RespondAsync(FriendRequestItemViewModel request, bool accept)
    {
        if (_client is null || Busy)
            return;

        Busy = true;
        try
        {
            await _client.RespondAsync(request.FromSteamId, accept);
            Requests.Remove(request);
            if (accept)
            {
                await ReloadFriendsAsync();
                Status = $"{request.DisplayName} est maintenant ton ami.";
            }
            else
            {
                Status = $"Demande de {request.DisplayName} refusée.";
            }
        }
        catch (AuthRequiredException) { await OnAuthLostAsync(); }
        catch (Exception ex) { Status = $"Échec : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task RemoveFriendAsync(FriendItemViewModel friend)
    {
        if (_client is null || Busy)
            return;

        Busy = true;
        try
        {
            await _client.RemoveFriendAsync(friend.SteamId);
            Friends.Remove(friend);
            Status = $"{friend.DisplayName} retiré.";
        }
        catch (AuthRequiredException) { await OnAuthLostAsync(); }
        catch (Exception ex) { Status = $"Échec : {ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task OnAuthLostAsync()
    {
        await LogoutAsync();
        Status = "Session expirée — reconnecte-toi avec Steam.";
    }

    private async Task ReloadFriendsAsync()
    {
        if (_client is null)
            return;

        List<FriendDto> friends = await _client.GetFriendsAsync();
        Friends.Clear();
        foreach (FriendDto f in friends)
            Friends.Add(new FriendItemViewModel(f, _client.AvatarUrl(f.SteamId)));
    }

    private async Task ReloadRequestsAsync()
    {
        if (_client is null)
            return;

        List<FriendRequestDto> requests = await _client.GetRequestsAsync();
        Requests.Clear();
        foreach (FriendRequestDto r in requests)
            Requests.Add(new FriendRequestItemViewModel(r, _client.AvatarUrl(r.FromSteamId)));
    }

    private void OnOnlineFriends(IReadOnlyList<string> onlineSteamIds)
    {
        var set = new HashSet<string>(onlineSteamIds);
        OnUi(() =>
        {
            foreach (FriendItemViewModel f in Friends)
                f.Online = set.Contains(f.SteamId);
            RaiseOnlineSummary();
        });
    }

    private void OnPresenceChanged(string steamId, bool online)
    {
        OnUi(() =>
        {
            FriendItemViewModel? f = Friends.FirstOrDefault(x => x.SteamId == steamId);
            if (f is not null)
                f.Online = online;
            RaiseOnlineSummary();
        });
    }

    /// <summary>Toast Windows demandé (titre, message) — câblé par MainViewModel vers le Notifier.</summary>
    public event Action<string, string>? ToastRequested;

    private void OnFriendRequestReceived() => OnUi(() =>
    {
        _ = ReloadRequestsAsync();
        Status = "Nouvelle demande d'ami reçue.";
        ToastRequested?.Invoke("Demande d'ami", "Tu as reçu une nouvelle demande d'ami.");
    });

    private void OnFriendsChanged() => OnUi(() =>
    {
        _ = ReloadFriendsAsync();
        _ = ReloadRequestsAsync();
    });

    private static void OnUi(Action action) => Application.Current?.Dispatcher.Invoke(action);

    private static string Format(string code) =>
        code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
}
