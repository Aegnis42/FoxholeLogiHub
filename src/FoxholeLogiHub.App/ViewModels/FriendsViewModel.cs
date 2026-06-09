using System.Collections.ObjectModel;
using System.Windows;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Gère la liste d'amis, les demandes reçues et la présence temps réel.
/// </summary>
public sealed class FriendsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;

    private Account? _account;
    private FriendsClient? _client;

    private string _apiBaseUrl;
    private string _myFriendCode = "—";
    private string _addCodeInput = "";
    private string _status = "Non connecté.";
    private bool _connected;
    private bool _busy;

    public FriendsViewModel()
    {
        _settings = _settingsStore.Load();
        _apiBaseUrl = _settings.ApiBaseUrl;
        Friends.CollectionChanged += (_, _) => Raise(nameof(HasNoFriends));
        Requests.CollectionChanged += (_, _) => Raise(nameof(HasRequests));
    }

    public ObservableCollection<FriendItemViewModel> Friends { get; } = new();
    public ObservableCollection<FriendRequestItemViewModel> Requests { get; } = new();

    public bool HasNoFriends => Friends.Count == 0;
    public bool HasRequests => Requests.Count > 0;

    public string MyFriendCode { get => _myFriendCode; private set => Set(ref _myFriendCode, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Connected { get => _connected; private set => Set(ref _connected, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public string AddCodeInput { get => _addCodeInput; set => Set(ref _addCodeInput, value); }
    public string ApiBaseUrl { get => _apiBaseUrl; set => Set(ref _apiBaseUrl, value); }

    public async Task InitializeAsync(Account account)
    {
        _account = account;
        await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (_account is null || Busy)
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
            _client = new FriendsClient(_settings.ApiBaseUrl);

            UserDto me = await _client.UpsertUserAsync(_account.SteamId, _account.DisplayName, _account.Faction.ToString());
            MyFriendCode = Format(me.FriendCode);

            // Partage l'avatar Steam local sur le serveur.
            await _client.UploadAvatarAsync(_account.SteamId, _account.AvatarPath ?? "");

            await ReloadFriendsAsync();
            await ReloadRequestsAsync();

            await _client.ConnectPresenceAsync(_account.SteamId, new PresenceHandlers(
                OnPresenceChanged, OnOnlineFriends, OnFriendRequestReceived, OnFriendsChanged));

            Connected = true;
            Status = $"Connecté · {Friends.Count} ami(s).";
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
        if (_client is null || _account is null)
        {
            Status = "Pas encore connecté au serveur.";
            return;
        }

        string code = AddCodeInput.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return;

        Busy = true;
        try
        {
            SendFriendRequestResult result = await _client.SendRequestAsync(_account.SteamId, code);
            AddCodeInput = "";
            if (result.Accepted)
            {
                await ReloadFriendsAsync();
                Status = $"{result.DisplayName} ajouté (vous vous étiez demandés mutuellement).";
            }
            else
            {
                Status = $"Demande envoyée à {result.DisplayName}.";
            }
        }
        catch (FriendException fex)
        {
            Status = fex.Message;
        }
        catch (Exception ex)
        {
            Status = $"Échec : {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task AcceptRequestAsync(FriendRequestItemViewModel request) => await RespondAsync(request, accept: true);
    public async Task DeclineRequestAsync(FriendRequestItemViewModel request) => await RespondAsync(request, accept: false);

    private async Task RespondAsync(FriendRequestItemViewModel request, bool accept)
    {
        if (_client is null || _account is null)
            return;

        Busy = true;
        try
        {
            await _client.RespondAsync(_account.SteamId, request.FromSteamId, accept);
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
        catch (Exception ex)
        {
            Status = $"Échec : {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task RemoveFriendAsync(FriendItemViewModel friend)
    {
        if (_client is null || _account is null)
            return;

        Busy = true;
        try
        {
            await _client.RemoveFriendAsync(_account.SteamId, friend.SteamId);
            Friends.Remove(friend);
            Status = $"{friend.DisplayName} retiré.";
        }
        catch (Exception ex)
        {
            Status = $"Échec : {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ReloadFriendsAsync()
    {
        if (_client is null || _account is null)
            return;

        List<FriendDto> friends = await _client.GetFriendsAsync(_account.SteamId);
        Friends.Clear();
        foreach (FriendDto f in friends)
            Friends.Add(new FriendItemViewModel(f, _client.AvatarUrl(f.SteamId)));
    }

    private async Task ReloadRequestsAsync()
    {
        if (_client is null || _account is null)
            return;

        List<FriendRequestDto> requests = await _client.GetRequestsAsync(_account.SteamId);
        Requests.Clear();
        foreach (FriendRequestDto r in requests)
            Requests.Add(new FriendRequestItemViewModel(r, _client.AvatarUrl(r.FromSteamId)));
    }

    // --- Événements temps réel (marshalés sur le thread UI) ---

    private void OnOnlineFriends(IReadOnlyList<string> onlineSteamIds)
    {
        var set = new HashSet<string>(onlineSteamIds);
        OnUi(() =>
        {
            foreach (FriendItemViewModel f in Friends)
                f.Online = set.Contains(f.SteamId);
        });
    }

    private void OnPresenceChanged(string steamId, bool online)
    {
        OnUi(() =>
        {
            FriendItemViewModel? f = Friends.FirstOrDefault(x => x.SteamId == steamId);
            if (f is not null)
                f.Online = online;
        });
    }

    private void OnFriendRequestReceived() => OnUi(() =>
    {
        _ = ReloadRequestsAsync();
        Status = "Nouvelle demande d'ami reçue.";
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
