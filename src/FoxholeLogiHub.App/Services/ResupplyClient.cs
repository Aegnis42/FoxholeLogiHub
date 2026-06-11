using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.Services;

/// <summary>Client HTTP authentifié (JWT) pour les demandes de ravitaillement.</summary>
public sealed class ResupplyClient : IDisposable
{
    private readonly HttpClient _http;

    public ResupplyClient(string baseUrl, string token)
    {
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { BaseAddress = new Uri(baseUrl.TrimEnd('/')), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<ResupplyRequestDto>> GetListAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/resupply");
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<ResupplyRequestDto>>()) ?? new();
    }

    public Task<List<ResupplyRequestDto>> CreateAsync(CreateResupplyRequest req) => PostAsync("/api/resupply", req);
    public Task<List<ResupplyRequestDto>> ClaimAsync(string id) => PostAsync("/api/resupply/claim", new ResupplyActionRequest(id));
    public Task<List<ResupplyRequestDto>> DoneAsync(string id) => PostAsync("/api/resupply/done", new ResupplyActionRequest(id));
    public Task<List<ResupplyRequestDto>> ReopenAsync(string id) => PostAsync("/api/resupply/reopen", new ResupplyActionRequest(id));
    public Task<List<ResupplyRequestDto>> DeleteAsync(string id) => PostAsync("/api/resupply/delete", new ResupplyActionRequest(id));
    public Task<List<ResupplyRequestDto>> SetVisibilityAsync(string id, int visibility) => PostAsync("/api/resupply/visibility", new SetResupplyVisibilityRequest(id, visibility));

    private async Task<List<ResupplyRequestDto>> PostAsync(string path, object body)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync(path, body);
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<ResupplyRequestDto>>()) ?? new();
    }

    private static async Task EnsureAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthRequiredException();
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new FriendException("Action non autorisée.");

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

    public void Dispose() => _http.Dispose();
}
