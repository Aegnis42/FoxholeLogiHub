using System.Net.Http;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoxholeLogiHub.App.Services;

/// <summary>Erreur métier renvoyée par l'API d'amis (message affichable à l'utilisateur).</summary>
public sealed class FriendException : Exception
{
    public FriendException(string message) : base(message) { }
}

/// <summary>
/// Client de l'API d'amis : HTTP pour les opérations CRUD + SignalR pour la présence temps réel.
/// </summary>
public sealed class FriendsClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private HubConnection? _hub;

    public FriendsClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<UserDto> UpsertUserAsync(string steamId, string displayName, string faction)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/users",
            new UpsertUserRequest(steamId, displayName, faction));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UserDto>())!;
    }

    public async Task<List<FriendDto>> GetFriendsAsync(string steamId) =>
        await _http.GetFromJsonAsync<List<FriendDto>>($"/api/friends/{steamId}") ?? new List<FriendDto>();

    public async Task<FriendDto> AddFriendAsync(string steamId, string friendCode)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/add",
            new AddFriendRequest(steamId, friendCode));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<FriendDto>())!;
    }

    public async Task RemoveFriendAsync(string steamId, string friendSteamId)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/remove",
            new RemoveFriendRequest(steamId, friendSteamId));
        await EnsureSuccessAsync(resp);
    }

    public bool IsPresenceConnected => _hub?.State == HubConnectionState.Connected;

    public async Task ConnectPresenceAsync(
        string steamId,
        Action<string, bool> onPresenceChanged,
        Action<IReadOnlyList<string>> onOnlineFriends)
    {
        await DisconnectPresenceAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hub/presence?steamId={Uri.EscapeDataString(steamId)}")
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, bool>(PresenceEvents.PresenceChanged, (id, online) => onPresenceChanged(id, online));
        _hub.On<List<string>>(PresenceEvents.OnlineFriends, list => onOnlineFriends(list));

        await _hub.StartAsync();
    }

    public async Task DisconnectPresenceAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;

        string message = "Erreur serveur.";
        try
        {
            ApiError? err = await resp.Content.ReadFromJsonAsync<ApiError>();
            if (err is not null && !string.IsNullOrWhiteSpace(err.Message))
                message = err.Message;
        }
        catch
        {
            // Corps non-JSON : on garde le message générique.
        }
        throw new FriendException(message);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectPresenceAsync();
        _http.Dispose();
    }
}
