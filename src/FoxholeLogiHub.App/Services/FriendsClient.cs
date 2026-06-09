using System.IO;
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

/// <summary>Callbacks d'événements temps réel du serveur.</summary>
public sealed record PresenceHandlers(
    Action<string, bool> OnPresenceChanged,
    Action<IReadOnlyList<string>> OnOnlineFriends,
    Action OnFriendRequestReceived,
    Action OnFriendsChanged);

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
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public string BaseUrl => _baseUrl;

    /// <summary>URL publique de l'avatar d'un utilisateur.</summary>
    public string AvatarUrl(string steamId) => $"{_baseUrl}/api/users/{steamId}/avatar";

    public async Task<UserDto> UpsertUserAsync(string steamId, string displayName, string faction)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/users",
            new UpsertUserRequest(steamId, displayName, faction));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UserDto>())!;
    }

    public async Task UploadAvatarAsync(string steamId, string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        // Best-effort : on n'échoue pas la connexion si l'avatar ne passe pas.
        try { await _http.PostAsync($"/api/users/{steamId}/avatar", content); }
        catch { /* ignore */ }
    }

    public async Task<List<FriendDto>> GetFriendsAsync(string steamId) =>
        await _http.GetFromJsonAsync<List<FriendDto>>($"/api/friends/{steamId}") ?? new List<FriendDto>();

    public async Task<List<FriendRequestDto>> GetRequestsAsync(string steamId) =>
        await _http.GetFromJsonAsync<List<FriendRequestDto>>($"/api/friends/requests/{steamId}") ?? new List<FriendRequestDto>();

    public async Task<SendFriendRequestResult> SendRequestAsync(string steamId, string friendCode)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/request",
            new SendFriendRequestRequest(steamId, friendCode));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<SendFriendRequestResult>())!;
    }

    public async Task RespondAsync(string steamId, string requesterSteamId, bool accept)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/respond",
            new RespondFriendRequestRequest(steamId, requesterSteamId, accept));
        await EnsureSuccessAsync(resp);
    }

    public async Task RemoveFriendAsync(string steamId, string friendSteamId)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/remove",
            new RemoveFriendRequest(steamId, friendSteamId));
        await EnsureSuccessAsync(resp);
    }

    public bool IsPresenceConnected => _hub?.State == HubConnectionState.Connected;

    public async Task ConnectPresenceAsync(string steamId, PresenceHandlers handlers)
    {
        await DisconnectPresenceAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hub/presence?steamId={Uri.EscapeDataString(steamId)}")
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, bool>(PresenceEvents.PresenceChanged, (id, online) => handlers.OnPresenceChanged(id, online));
        _hub.On<List<string>>(PresenceEvents.OnlineFriends, list => handlers.OnOnlineFriends(list));
        _hub.On(PresenceEvents.FriendRequestReceived, handlers.OnFriendRequestReceived);
        _hub.On(PresenceEvents.FriendsChanged, handlers.OnFriendsChanged);

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
            // Corps non-JSON : message générique.
        }
        throw new FriendException(message);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectPresenceAsync();
        _http.Dispose();
    }
}
