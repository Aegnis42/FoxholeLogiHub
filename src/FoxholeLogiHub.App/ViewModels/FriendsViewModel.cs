using System.Collections.ObjectModel;
using System.Windows;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Gère la liste d'amis et la présence temps réel. Identifie l'utilisateur par son Steam ID
/// (compte), récupère son code d'ami, et tient la liste à jour via SignalR.
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
    }

    public ObservableCollection<FriendItemViewModel> Friends { get; } = new();

    public bool HasNoFriends => Friends.Count == 0;

    public string MyFriendCode { get => _myFriendCode; private set => Set(ref _myFriendCode, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Connected { get => _connected; private set => Set(ref _connected, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public string AddCodeInput
    {
        get => _addCodeInput;
        set => Set(ref _addCodeInput, value);
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => Set(ref _apiBaseUrl, value);
    }

    /// <summary>Appelé par le shell une fois le compte chargé.</summary>
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
            // Sauvegarde l'URL choisie.
            _settings.ApiBaseUrl = ApiBaseUrl.Trim();
            _settingsStore.Save(_settings);

            if (_client is not null)
                await _client.DisposeAsync();
            _client = new FriendsClient(_settings.ApiBaseUrl);

            UserDto me = await _client.UpsertUserAsync(_account.SteamId, _account.DisplayName, _account.Faction.ToString());
            MyFriendCode = Format(me.FriendCode);

            await ReloadFriendsAsync();

            await _client.ConnectPresenceAsync(_account.SteamId, OnPresenceChanged, OnOnlineFriends);

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

    public async Task AddFriendAsync()
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
            FriendDto friend = await _client.AddFriendAsync(_account.SteamId, code);
            AddCodeInput = "";
            await ReloadFriendsAsync();
            Status = $"{friend.DisplayName} ajouté.";
        }
        catch (FriendException fex)
        {
            Status = fex.Message;
        }
        catch (Exception ex)
        {
            Status = $"Échec de l'ajout : {ex.Message}";
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
            Friends.Add(new FriendItemViewModel(f));
    }

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

    private static void OnUi(Action action)
    {
        Application.Current?.Dispatcher.Invoke(action);
    }

    /// <summary>Affiche un code "ABC123" sous la forme "ABC-123".</summary>
    private static string Format(string code) =>
        code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
}
