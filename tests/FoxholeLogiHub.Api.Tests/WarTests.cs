using System.Net;
using System.Net.Http.Json;
using FoxholeLogiHub.Contracts;
using static FoxholeLogiHub.Api.Tests.TestWorld;

namespace FoxholeLogiHub.Api.Tests;

/// <summary>
/// Intégration War : statut indisponible quand le rafraîchissement est désactivé (tests),
/// contrôle de ville « unknown » sur les stockpiles, et reset de fin de guerre (chef uniquement).
/// </summary>
public sealed class WarTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public WarTests(ApiFactory factory) => _factory = factory;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Statut_de_guerre_indisponible_sans_rafraichissement()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var war = await GetAsync<WarStatusDto>(a, "/api/war");
        Assert.False(war.Available);
    }

    [Fact]
    public async Task Stockpile_sans_donnees_war_est_en_controle_inconnu()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        string t = Tag();
        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles",
            new CreateStockpileRequest($"Dep-{t}", "Deadlands", "The Gallows", StockpileTypes.StorageDepot, "1", false));
        var sp = list.Single(s => s.Name == $"Dep-{t}");
        Assert.Equal(WarTownControl.Unknown, sp.TownControl);
        Assert.False(sp.TownScorched);
    }

    [Fact]
    public async Task Reset_de_fin_de_guerre_purge_tout_et_reste_reserve_au_chef()
    {
        var (chef, regA) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var membre = _factory.ClientFor(NewSteamId());
        await PostAsync(membre, "/api/users", new UpsertUserRequest("Membre", "Wardens"));
        await PostAsync(membre, "/api/regiments/join", new JoinRegimentRequest(regA.InviteCode));
        string t = Tag();

        // Données à purger : un stockpile avec item + une demande.
        var list = await PostAsync<List<StockpileDto>>(chef, "/api/stockpiles",
            new CreateStockpileRequest($"Dep-{t}", "Deadlands", "", StockpileTypes.StorageDepot, "1", false));
        string spId = list.Single(s => s.Name == $"Dep-{t}").Id;
        await PostAsync(chef, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(spId, "Bandages", "Bandages", "Médical", 50, 0, 0));
        await PostAsync(chef, "/api/resupply", new CreateResupplyRequest($"Demande-{t}", "Deadlands", "",
            new List<ResupplyItemDto> { new("Bandages", "Bandages", "Médical", 10) }, 0, "", ResupplyVisibility.Regiment));

        // Un simple membre ne peut pas reset.
        var refused = await membre.PostAsync("/api/regiments/war-reset", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Le chef oui — tout est purgé.
        var resp = await chef.PostAsync("/api/regiments/war-reset", null);
        Assert.True(resp.IsSuccessStatusCode);
        var result = await resp.Content.ReadFromJsonAsync<WarResetResultDto>();
        Assert.True(result!.Stockpiles >= 1);
        Assert.True(result.Items >= 1);
        Assert.True(result.Requests >= 1);

        var stockpiles = await GetAsync<List<StockpileDto>>(chef, "/api/stockpiles");
        Assert.DoesNotContain(stockpiles, s => s.Name == $"Dep-{t}");
        var requests = await GetAsync<List<ResupplyRequestDto>>(chef, "/api/resupply");
        Assert.DoesNotContain(requests, r => r.Title == $"Demande-{t}");
    }
}
