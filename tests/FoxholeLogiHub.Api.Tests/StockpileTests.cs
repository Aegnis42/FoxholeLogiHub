using System.Net;
using FoxholeLogiHub.Contracts;
using static FoxholeLogiHub.Api.Tests.TestWorld;

namespace FoxholeLogiHub.Api.Tests;

/// <summary>
/// Verrouille les règles des stockpiles : permission ManageStockpiles, visibilité public/privé +
/// partage aux alliés, alertes (publics exclus), import (dédoublonnage, seuils préservés, bornes).
/// </summary>
public sealed class StockpileTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public StockpileTests(ApiFactory factory) => _factory = factory;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static CreateStockpileRequest Sp(string name, bool isPublic) =>
        new(name, "Deadlands", "The Gallows", StockpileTypes.StorageDepot, "123456", isPublic);

    [Fact]
    public async Task Membre_sans_permission_ne_peut_pas_creer_de_stockpile()
    {
        var (a, regA) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var member = _factory.ClientFor(NewSteamId());
        await PostAsync(member, "/api/users", new UpsertUserRequest("Membre", "Wardens"));
        await PostAsync(member, "/api/regiments/join", new JoinRegimentRequest(regA.InviteCode));

        var refused = await PostRawAsync(member, "/api/stockpiles", Sp($"Interdit-{Tag()}", false));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var allowed = await PostRawAsync(a, "/api/stockpiles", Sp($"Autorise-{Tag()}", false));
        Assert.True(allowed.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Visibilite_public_allie_et_partage_du_prive()
    {
        var (a, regA) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var (b, regB) = await UserWithRegimentAsync(_factory, "Bravo", "BRV");
        var (c, _) = await UserWithRegimentAsync(_factory, "Charlie", "CHA");
        await AllyAsync(a, regA, b, regB);
        string t = Tag();

        await PostAsync(a, "/api/stockpiles", Sp($"Pub-{t}", true));
        var aList = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Priv-{t}", false));
        string privId = aList.Single(s => s.Name == $"Priv-{t}").Id;

        var bList = await GetAsync<List<StockpileDto>>(b, "/api/stockpiles");
        Assert.Contains(bList, s => s.Name == $"Pub-{t}");
        Assert.DoesNotContain(bList, s => s.Name == $"Priv-{t}");

        var cList = await GetAsync<List<StockpileDto>>(c, "/api/stockpiles");
        Assert.DoesNotContain(cList, s => s.Name == $"Pub-{t}");   // pas allié → rien
        Assert.DoesNotContain(cList, s => s.Name == $"Priv-{t}");

        await PostAsync(a, "/api/stockpiles/share", new ShareStockpileRequest(privId, regB.Id));
        bList = await GetAsync<List<StockpileDto>>(b, "/api/stockpiles");
        Assert.Contains(bList, s => s.Name == $"Priv-{t}");
    }

    [Fact]
    public async Task Alertes_ignorent_les_stockpiles_publics()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        string t = Tag();

        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Pub-{t}", true));
        string pubId = list.Single(s => s.Name == $"Pub-{t}").Id;
        list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Priv-{t}", false));
        string privId = list.Single(s => s.Name == $"Priv-{t}").Id;

        await PostAsync(a, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(pubId, "RifleAmmo", "Rifle Ammo", "Munitions", 10, 100, 50));
        await PostAsync(a, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(privId, "RifleAmmo", "Rifle Ammo", "Munitions", 10, 100, 50));

        var alerts = await GetAsync<List<StockpileAlertDto>>(a, "/api/stockpiles/alerts");
        Assert.Contains(alerts, x => x.StockpileName == $"Priv-{t}");
        Assert.DoesNotContain(alerts, x => x.StockpileName == $"Pub-{t}");
    }

    [Fact]
    public async Task Import_dedoublonne_et_preserve_les_seuils_des_items_absents()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        string t = Tag();
        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Dep-{t}", false));
        string id = list.Single(s => s.Name == $"Dep-{t}").Id;

        // Seuils posés sur deux items suivis.
        await PostAsync(a, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(id, "RifleAmmo", "Rifle Ammo", "Munitions", 30, 200, 50));
        await PostAsync(a, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(id, "Bandages", "Bandages", "Médical", 80, 300, 100));

        // L'import (capture) ne contient que RifleAmmo, en double (caisse + unité).
        var items = await PostAsync<List<StockpileItemDto>>(a, "/api/stockpiles/items/import",
            new ImportStockpileItemsRequest(id, new List<StockpileItemDto>
            {
                new("RifleAmmo", "Rifle Ammo", "Munitions", 40, 0, 0),
                new("RifleAmmo", "Rifle Ammo", "Munitions", 1200, 0, 0),
            }));

        var rifle = items.Single(i => i.Code == "RifleAmmo");
        Assert.Equal(1240, rifle.Quantity);          // dédoublonné (somme)
        Assert.Equal(200, rifle.LowThreshold);       // seuils préservés
        Assert.Equal(50, rifle.CriticalThreshold);

        var bandages = items.Single(i => i.Code == "Bandages");
        Assert.Equal(0, bandages.Quantity);          // absent de la capture → reste à 0
        Assert.Equal(300, bandages.LowThreshold);    // ... sans perdre ses seuils
        Assert.Equal(100, bandages.CriticalThreshold);
    }

    [Fact]
    public async Task Import_trop_gros_rejete_et_quantites_bornees()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        string t = Tag();
        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Dep-{t}", false));
        string id = list.Single(s => s.Name == $"Dep-{t}").Id;

        var tooBig = Enumerable.Range(0, 2001)
            .Select(i => new StockpileItemDto($"Item{i}", $"Item {i}", "x", 1, 0, 0)).ToList();
        var refused = await PostRawAsync(a, "/api/stockpiles/items/import", new ImportStockpileItemsRequest(id, tooBig));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var items = await PostAsync<List<StockpileItemDto>>(a, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(id, "Cloth", "Bmats", "Ressources", int.MaxValue, 0, 0));
        Assert.True(items.Single(i => i.Code == "Cloth").Quantity <= 10_000_000); // borné
    }

    [Fact]
    public async Task Type_de_stockpile_inconnu_rejete_et_titre_tronque()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");

        var badType = await PostRawAsync(a, "/api/stockpiles",
            new CreateStockpileRequest($"X-{Tag()}", "Deadlands", "", "ChateauGonflable", "", false));
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);

        // Nom démesuré → tronqué côté serveur (pas de stockage arbitraire).
        string huge = new string('x', 5000);
        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles",
            new CreateStockpileRequest(huge, "Deadlands", "", StockpileTypes.StorageDepot, "", false));
        Assert.All(list.Where(s => s.Name.StartsWith("xxx")), s => Assert.True(s.Name.Length <= 64));
    }

    [Fact]
    public async Task Historique_borne_la_frequence_des_instantanes()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        string t = Tag();
        var list = await PostAsync<List<StockpileDto>>(a, "/api/stockpiles", Sp($"Dep-{t}", false));
        string id = list.Single(s => s.Name == $"Dep-{t}").Id;

        // Trois imports rapprochés (< 15 min) → un seul instantané conservé par item.
        for (int q = 100; q <= 300; q += 100)
            await PostAsync<List<StockpileItemDto>>(a, "/api/stockpiles/items/import",
                new ImportStockpileItemsRequest(id, new List<StockpileItemDto>
                {
                    new("Cloth", "Bmats", "Ressources", q, 0, 0),
                }));

        var history = await GetAsync<List<StockpileItemHistoryDto>>(a, $"/api/stockpiles/{id}/history");
        var cloth = history.Single(h => h.Code == "Cloth");
        Assert.Single(cloth.Points); // le rate-limit 15 min n'a gardé que le premier
    }
}

/// <summary>Le neutraliseur Discord casse les mentions de masse et le markdown (anti-injection webhook).</summary>
public sealed class DiscordSafeTests
{
    [Theory]
    [InlineData("@everyone alerte", "everyone alerte")]
    [InlineData("Dépôt\nligne 2", "Dépôt ligne 2")]
    [InlineData("**gras** `code`", "gras code")]
    [InlineData("<@1234>", "1234")]
    public void Safe_neutralise_mentions_et_markdown(string input, string expected)
    {
        Assert.Equal(expected, FoxholeLogiHub.Api.Common.DiscordNotifier.Safe(input).Trim());
    }

    [Fact]
    public void Safe_tronque_les_textes_trop_longs()
    {
        string huge = new string('x', 200);
        Assert.True(FoxholeLogiHub.Api.Common.DiscordNotifier.Safe(huge).Length <= 81);
    }
}
