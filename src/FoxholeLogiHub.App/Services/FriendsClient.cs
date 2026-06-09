using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoxholeLogiHub.App.Services;

/// <summary>Erreur métier renvoyée par l'API d'amis (message affichable à l'utilisateur).</summary>
public sealed class FriendException : Exception
{
    public FriendException(string message) : base(message) { }
}

/// <summary>Le jeton est absent/expiré : une reconnexion Steam est nécessaire.</summary>
public sealed class AuthRequiredException : Exception { }

/// <summary>Callbacks d'événements temps réel du serveur.</summary>
public sealed record PresenceHandlers(
    Action<string, bool> OnPresenceChanged,
    Action<IReadOnlyList<string>> OnOnlineFriends,
    Action OnFriendRequestReceived,
    Action OnFriendsChanged,
    Action OnRegimentChanged,
    Action OnRegimentInviteReceived);

/// <summary>
/// Client de l'API d'amis, authentifié par JWT (en-tête Bearer pour HTTP, access_token pour SignalR).
/// L'identité (Steam ID) est portée par le jeton — plus jamais envoyée dans le corps.
/// </summary>
public sealed class FriendsClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token;
    private HubConnection? _hub;

    public FriendsClient(string baseUrl, string token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string AvatarUrl(string steamId) => $"{_baseUrl}/api/users/{steamId}/avatar";

    public async Task<UserDto> UpsertUserAsync(string displayName, string faction)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/users", new UpsertUserRequest(displayName, faction));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<UserDto>())!;
    }

    public async Task UploadAvatarAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        try { await _http.PostAsync("/api/users/avatar", content); }
        catch { /* best-effort */ }
    }

    public async Task<List<FriendDto>> GetFriendsAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/friends");
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<FriendDto>>()) ?? new List<FriendDto>();
    }

    public async Task<List<FriendRequestDto>> GetRequestsAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/friends/requests");
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<FriendRequestDto>>()) ?? new List<FriendRequestDto>();
    }

    public async Task<SendFriendRequestResult> SendRequestAsync(string friendCode)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/request", new SendFriendRequestRequest(friendCode));
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<SendFriendRequestResult>())!;
    }

    public async Task RespondAsync(string requesterSteamId, bool accept)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/respond", new RespondFriendRequestRequest(requesterSteamId, accept));
        await EnsureSuccessAsync(resp);
    }

    public async Task RemoveFriendAsync(string friendSteamId)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/friends/remove", new RemoveFriendRequest(friendSteamId));
        await EnsureSuccessAsync(resp);
    }

    public async Task ConnectPresenceAsync(PresenceHandlers handlers)
    {
        await DisconnectPresenceAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hub/presence", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_token);
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, bool>(PresenceEvents.PresenceChanged, (id, online) => handlers.OnPresenceChanged(id, online));
        _hub.On<List<string>>(PresenceEvents.OnlineFriends, list => handlers.OnOnlineFriends(list));
        _hub.On(PresenceEvents.FriendRequestReceived, handlers.OnFriendRequestReceived);
        _hub.On(PresenceEvents.FriendsChanged, handlers.OnFriendsChanged);
        _hub.On(PresenceEvents.RegimentChanged, handlers.OnRegimentChanged);
        _hub.On(PresenceEvents.RegimentInviteReceived, handlers.OnRegimentInviteReceived);

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

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthRequiredException();

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
