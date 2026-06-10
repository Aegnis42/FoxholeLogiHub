using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.Services;

/// <summary>Client HTTP authentifié (JWT) pour les stockpiles.</summary>
public sealed class StockpileClient : IDisposable
{
    private readonly HttpClient _http;

    public StockpileClient(string baseUrl, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<StockpileDto>> GetListAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/stockpiles");
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileDto>>()) ?? new();
    }

    public Task<List<StockpileDto>> CreateAsync(CreateStockpileRequest req) => PostListAsync("/api/stockpiles", req, HttpMethod.Post);
    public Task<List<StockpileDto>> UpdateAsync(UpdateStockpileRequest req) => PostListAsync("/api/stockpiles", req, HttpMethod.Put);
    public Task<List<StockpileDto>> DeleteAsync(string id) => PostListAsync("/api/stockpiles/delete", new DeleteStockpileRequest(id), HttpMethod.Post);
    public Task<List<StockpileDto>> ShareAsync(string id, string regimentId) => PostListAsync("/api/stockpiles/share", new ShareStockpileRequest(id, regimentId), HttpMethod.Post);
    public Task<List<StockpileDto>> UnshareAsync(string id, string regimentId) => PostListAsync("/api/stockpiles/unshare", new UnshareStockpileRequest(id, regimentId), HttpMethod.Post);

    public async Task<List<StockpileAlertDto>> GetAlertsAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/stockpiles/alerts");
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileAlertDto>>()) ?? new();
    }

    /// <summary>État de la guerre en cours (numéro, jour, points de victoire) — cache serveur.</summary>
    public async Task<WarStatusDto?> GetWarStatusAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/war");
        await EnsureAsync(resp);
        return await resp.Content.ReadFromJsonAsync<WarStatusDto>();
    }

    /// <summary>Carte du monde : contrôle des villes par hexagone (cache serveur).</summary>
    public async Task<WarMapDto?> GetWarMapAsync()
    {
        HttpResponseMessage resp = await _http.GetAsync("/api/war/map");
        await EnsureAsync(resp);
        return await resp.Content.ReadFromJsonAsync<WarMapDto>();
    }

    public async Task<List<StockpileItemDto>> GetItemsAsync(string stockpileId)
    {
        HttpResponseMessage resp = await _http.GetAsync($"/api/stockpiles/{stockpileId}/items");
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileItemDto>>()) ?? new();
    }

    public async Task<List<StockpileItemDto>> SetItemAsync(SetStockpileItemRequest req)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/stockpiles/items/set", req);
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileItemDto>>()) ?? new();
    }

    public async Task<List<StockpileItemDto>> ImportItemsAsync(string stockpileId, List<StockpileItemDto> items)
    {
        HttpResponseMessage resp = await _http.PostAsJsonAsync("/api/stockpiles/items/import",
            new ImportStockpileItemsRequest(stockpileId, items));
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileItemDto>>()) ?? new();
    }

    private async Task<List<StockpileDto>> PostListAsync(string path, object body, HttpMethod method)
    {
        using var req = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        HttpResponseMessage resp = await _http.SendAsync(req);
        await EnsureAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<StockpileDto>>()) ?? new();
    }

    private static async Task EnsureAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthRequiredException();
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new FriendException("Tu n'as pas la permission de gérer les stockpiles.");

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
