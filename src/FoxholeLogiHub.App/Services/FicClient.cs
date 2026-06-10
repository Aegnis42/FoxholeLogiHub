using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FoxholeLogiHub.App.Services;

/// <summary>Client du companion FIR : envoie une capture, reçoit les items reconnus (codename + quantité).</summary>
public sealed class FicClient : IDisposable
{
    private readonly HttpClient _http;

    public FicClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')), Timeout = TimeSpan.FromSeconds(30) };
    }

    public void Dispose() => _http.Dispose();

    public async Task<List<(string Code, int Quantity, bool IsCrated)>> ExtractAsync(byte[] png)
    {
        using var content = new ByteArrayContent(png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        HttpResponseMessage resp = await _http.PostAsync("/extract", content);
        resp.EnsureSuccessStatusCode();

        string json = await resp.Content.ReadAsStringAsync();
        var items = new List<(string, int, bool)>();

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return items; // null = aucune stockpile détectée
        if (!doc.RootElement.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
            return items;

        foreach (JsonElement e in contents.EnumerateArray())
        {
            string? code = null;
            bool crated = false;
            if (e.TryGetProperty("icon", out var icon))
            {
                if (icon.TryGetProperty("code_name", out var cn) && cn.ValueKind == JsonValueKind.String)
                    code = cn.GetString();
                if (icon.TryGetProperty("is_crated", out var ic) && ic.ValueKind == JsonValueKind.True)
                    crated = true;
            }

            int qty = 0;
            if (e.TryGetProperty("quantity", out var q) && q.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                qty = v.GetInt32();

            if (!string.IsNullOrEmpty(code) && qty > 0)
                items.Add((code!, qty, crated));
        }
        return items;
    }
}
