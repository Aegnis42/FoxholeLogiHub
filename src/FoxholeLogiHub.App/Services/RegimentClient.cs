using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.Services;

/// <summary>Client HTTP authentifié (JWT) pour le module régiment.</summary>
public sealed class RegimentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public RegimentClient(string baseUrl, string token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string AvatarUrl(string steamId) => $"{_baseUrl}/api/users/{steamId}/avatar";

    public async Task<RegimentDto?> GetMineAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/regiments/mine");
        await EnsureAsync(resp);
        return await resp.Content.ReadFromJsonAsync<RegimentDto>();
    }

    public async Task<List<RegimentInviteDto>> GetInvitesAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/regiments/invites");
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<RegimentInviteDto>>()) ?? new();
    }

    public Task<RegimentDto?> CreateAsync(string name, string tag, string faction) =>
        PostDtoAsync("/api/regiments", new CreateRegimentRequest(name, tag, faction));

    public Task<RegimentDto?> JoinAsync(string code) =>
        PostDtoAsync("/api/regiments/join", new JoinRegimentRequest(code));

    public Task LeaveAsync() => SendAsync(HttpMethod.Post, "/api/regiments/leave", null);
    public Task DeleteAsync() => SendAsync(HttpMethod.Delete, "/api/regiments", null);

    public Task<RegimentDto?> UpdateAsync(string name, string tag) =>
        PutDtoAsync("/api/regiments", new UpdateRegimentRequest(name, tag));

    public async Task<string> RegenerateCodeAsync()
    {
        HttpResponseMessage resp = await _http.PostAsync("/api/regiments/regenerate-code", null);
        await EnsureAsync(resp);
        var obj = await resp.Content.ReadFromJsonAsync<RegenCodeResponse>();
        return obj?.InviteCode ?? "";
    }

    public Task<RegimentDto?> CreateRoleAsync(string name, int perms) =>
        PostDtoAsync("/api/regiments/roles", new CreateRoleRequest(name, perms));
    public Task<RegimentDto?> UpdateRoleAsync(int id, string name, int perms) =>
        PutDtoAsync("/api/regiments/roles", new UpdateRoleRequest(id, name, perms));
    public Task<RegimentDto?> DeleteRoleAsync(int id) =>
        PostDtoAsync("/api/regiments/roles/delete", new UpdateRoleRequest(id, "", 0));

    public Task<RegimentDto?> SetMemberRoleAsync(string steamId, int roleId) =>
        PostDtoAsync("/api/regiments/members/role", new SetMemberRoleRequest(steamId, roleId));
    public Task<RegimentDto?> KickAsync(string steamId) =>
        PostDtoAsync("/api/regiments/members/kick", new KickMemberRequest(steamId));

    public Task InviteFriendAsync(string friendSteamId) =>
        SendAsync(HttpMethod.Post, "/api/regiments/invite", new InviteFriendToRegimentRequest(friendSteamId));
    public Task RespondInviteAsync(string regimentId, bool accept) =>
        SendAsync(HttpMethod.Post, "/api/regiments/invites/respond", new RespondRegimentInviteRequest(regimentId, accept));

    /// <summary>Fin de guerre (chef) : purge stockpiles + demandes du régiment côté serveur.</summary>
    public async Task<WarResetResultDto> WarResetAsync()
    {
        HttpResponseMessage resp = await _http.PostAsync("/api/regiments/war-reset", null);
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<WarResetResultDto>()) ?? new WarResetResultDto(0, 0, 0);
    }

    public Task ProposeAllianceAsync(string code) =>
        SendAsync(HttpMethod.Post, "/api/regiments/alliances/propose", new ProposeAllianceRequest(code));
    public Task RespondAllianceAsync(string otherId, bool accept) =>
        SendAsync(HttpMethod.Post, "/api/regiments/alliances/respond", new RespondAllianceRequest(otherId, accept));
    public Task RemoveAllianceAsync(string otherId) =>
        SendAsync(HttpMethod.Post, "/api/regiments/alliances/remove", new RemoveAllianceRequest(otherId));

    private async Task<RegimentDto?> PostDtoAsync(string path, object body)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync(path, body);
        await EnsureAsync(resp);
        return await resp.Content.ReadFromJsonAsync<RegimentDto>();
    }

    private async Task<RegimentDto?> PutDtoAsync(string path, object body)
    {
        HttpResponseMessage resp = await _http.PutAsJsonAsync(path, body);
        await EnsureAsync(resp);
        return await resp.Content.ReadFromJsonAsync<RegimentDto>();
    }

    private async Task SendAsync(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        HttpResponseMessage resp = await _http.SendAsync(req);
        await EnsureAsync(resp);
    }

    private static async Task EnsureAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthRequiredException();
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new FriendException("Tu n'as pas la permission pour cette action.");

        string message = "Erreur serveur.";
        try
        {
            ApiError? err = await resp.Content.ReadFromJsonAsync<ApiError>();
            if (err is not null && !string.IsNullOrWhiteSpace(err.Message))
                message = err.Message;
        }
        catch { }
        throw new FriendException(message);
    }

    private sealed record RegenCodeResponse(string InviteCode);

    public void Dispose() => _http.Dispose();
}
