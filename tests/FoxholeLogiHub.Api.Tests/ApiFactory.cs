using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FoxholeLogiHub.Api.Tests;

/// <summary>
/// Héberge l'API en mémoire sur une base SQLite temporaire (un fichier par fixture, supprimé à la fin).
/// Les jetons sont émis par le vrai TokenService (secret de dev — JWT_SECRET absent en test).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"foxhole-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");

    /// <summary>Client HTTP authentifié pour un Steam ID arbitraire.</summary>
    public HttpClient ClientFor(string steamId)
    {
        string token = Services.GetRequiredService<TokenService>().Issue(steamId);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }
}

/// <summary>Petits helpers HTTP pour des scénarios lisibles (créer un monde par test, isolé par Steam IDs uniques).</summary>
internal static class TestWorld
{
    private static int _seq;

    /// <summary>Steam ID unique par appel — chaque test construit son propre monde dans la base partagée.</summary>
    public static string NewSteamId() => $"7656119{Interlocked.Increment(ref _seq):D6}{Random.Shared.Next(100, 999)}";

    /// <summary>Crée un utilisateur + son régiment (il en est le chef) et renvoie le client + le régiment.</summary>
    public static async Task<(HttpClient Client, RegimentDto Reg)> UserWithRegimentAsync(ApiFactory factory, string name, string tag)
    {
        var client = factory.ClientFor(NewSteamId());
        await PostAsync(client, "/api/users", new UpsertUserRequest(name, "Wardens"));
        var reg = await PostAsync<RegimentDto>(client, "/api/regiments", new CreateRegimentRequest(name, tag, "Wardens"));
        return (client, reg);
    }

    /// <summary>Alliance acceptée entre deux régiments (A propose, B accepte).</summary>
    public static async Task AllyAsync(HttpClient a, RegimentDto regA, HttpClient b, RegimentDto regB)
    {
        await PostAsync(a, "/api/regiments/alliances/propose", new ProposeAllianceRequest(regB.InviteCode));
        await PostAsync(b, "/api/regiments/alliances/respond", new RespondAllianceRequest(regA.Id, true));
    }

    public static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, object body) =>
        await client.PostAsJsonAsync(path, body);

    public static async Task PostAsync(HttpClient client, string path, object body)
    {
        var resp = await client.PostAsJsonAsync(path, body);
        Assert.True(resp.IsSuccessStatusCode, $"POST {path} → {(int)resp.StatusCode} : {await resp.Content.ReadAsStringAsync()}");
    }

    public static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        var resp = await client.PostAsJsonAsync(path, body);
        Assert.True(resp.IsSuccessStatusCode, $"POST {path} → {(int)resp.StatusCode} : {await resp.Content.ReadAsStringAsync()}");
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var resp = await client.GetAsync(path);
        Assert.True(resp.IsSuccessStatusCode, $"GET {path} → {(int)resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }
}
