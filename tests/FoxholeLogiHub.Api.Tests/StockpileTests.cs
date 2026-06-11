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
    public async Task Permissions_granulaires_et_parapluie_ManageStockpiles()
    {
        var (chef, reg) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var member = _factory.ClientFor(NewSteamId());
        await PostAsync(member, "/api/users", new UpsertUserRequest("Logi", "Wardens"));
        await PostAsync(member, "/api/regiments/join", new JoinRegimentRequest(reg.InviteCode));
        string memberId = (await GetAsync<RegimentDto>(member, "/api/regiments/mine"))!
            .Members.Single(m => m.DisplayName == "Logi").SteamId;
        string t = Tag();

        // Rôle granulaire : créer (privé) + modifier le contenu — ni partage, ni suppression.
        int perms = (int)(RegimentPermission.StockpileCreate | RegimentPermission.StockpileEdit);
        var dto = await PostAsync<RegimentDto>(chef, "/api/regiments/roles", new CreateRoleRequest($"Logi-{t}", perms));
        int roleId = dto!.Roles.Single(r => r.Name == $"Logi-{t}").Id;
        await PostAsync(chef, "/api/regiments/members/role", new SetMemberRoleRequest(memberId, roleId));

        // Créer un PRIVÉ : oui. Créer un PUBLIC (alliance) : non — StockpileShare manquant.
        var okPriv = await PostRawAsync(member, "/api/stockpiles", Sp($"G-Priv-{t}", false));
        Assert.True(okPriv.IsSuccessStatusCode);
        var koPub = await PostRawAsync(member, "/api/stockpiles", Sp($"G-Pub-{t}", true));
        Assert.Equal(HttpStatusCode.Forbidden, koPub.StatusCode);

        // Modifier le contenu : oui. Supprimer : non.
        var list = await GetAsync<List<StockpileDto>>(member, "/api/stockpiles");
        string spId = list.Single(s => s.Name == $"G-Priv-{t}").Id;
        var okSet = await PostRawAsync(member, "/api/stockpiles/items/set",
            new SetStockpileItemRequest(spId, "RifleAmmo", "Rifle Ammo", "Munitions", 100, 0, 0));
        Assert.True(okSet.IsSuccessStatusCode);
        var koDel = await PostRawAsync(member, "/api/stockpiles/delete", new DeleteStockpileRequest(spId));
        Assert.Equal(HttpStatusCode.Forbidden, koDel.StatusCode);

        // Parapluie : ManageStockpiles seul couvre TOUT (compatibilité des rôles existants).
        var dto2 = await PostAsync<RegimentDto>(chef, "/api/regiments/roles",
            new CreateRoleRequest($"Admin-{t}", (int)RegimentPermission.ManageStockpiles));
        int adminRoleId = dto2!.Roles.Single(r => r.Name == $"Admin-{t}").Id;
        await PostAsync(chef, "/api/regiments/members/role", new SetMemberRoleRequest(memberId, adminRoleId));
        var okDel = await PostRawAsync(member, "/api/stockpiles/delete", new DeleteStockpileRequest(spId));
        Assert.True(okDel.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Permissions_granulaires_ravitaillement()
    {
        var (chef, reg) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var member = _factory.ClientFor(NewSteamId());
        await PostAsync(member, "/api/users", new UpsertUserRequest("Logi", "Wardens"));
        await PostAsync(member, "/api/regiments/join", new JoinRegimentRequest(reg.InviteCode));
        string memberId = (await GetAsync<RegimentDto>(member, "/api/regiments/mine"))!
            .Members.Single(m => m.DisplayName == "Logi").SteamId;
        string t = Tag();
        var items = new List<ResupplyItemDto> { new("RifleAmmo", "Rifle Ammo", "Munitions", 100) };

        // Rôle sans permission : demander à SON régiment (0) est libre ; publier au-delà
        // (alliance 1 / publique 2) est refusé sans « Publier des demandes ».
        var dtoNone = await PostAsync<RegimentDto>(chef, "/api/regiments/roles", new CreateRoleRequest($"Rien-{t}", 0));
        int noneId = dtoNone!.Roles.Single(r => r.Name == $"Rien-{t}").Id;
        await PostAsync(chef, "/api/regiments/members/role", new SetMemberRoleRequest(memberId, noneId));
        var okReg = await PostRawAsync(member, "/api/resupply", new CreateResupplyRequest($"Reg-{t}", "Deadlands", "", items, 1, "", ResupplyVisibility.Regiment));
        Assert.True(okReg.IsSuccessStatusCode);
        var koAll = await PostRawAsync(member, "/api/resupply", new CreateResupplyRequest($"KOA-{t}", "Deadlands", "", items, 1, "", ResupplyVisibility.Alliance));
        Assert.Equal(HttpStatusCode.Forbidden, koAll.StatusCode);
        var koPub = await PostRawAsync(member, "/api/resupply", new CreateResupplyRequest($"KOP-{t}", "Deadlands", "", items, 1, "", ResupplyVisibility.Public));
        Assert.Equal(HttpStatusCode.Forbidden, koPub.StatusCode);

        // Exposer sa propre demande régiment vers l'alliance : refusé sans la permission aussi.
        var myList = await GetAsync<List<ResupplyRequestDto>>(member, "/api/resupply");
        string myReqId = myList!.Single(r => r.Title == $"Reg-{t}").Id;
        var koVis = await PostRawAsync(member, "/api/resupply/visibility", new SetResupplyVisibilityRequest(myReqId, ResupplyVisibility.Alliance));
        Assert.Equal(HttpStatusCode.Forbidden, koVis.StatusCode);

        // ResupplyShare : publier à l'alliance oui ; livrer/supprimer la demande d'un AUTRE : non.
        var dtoShare = await PostAsync<RegimentDto>(chef, "/api/regiments/roles",
            new CreateRoleRequest($"Pub-{t}", (int)RegimentPermission.ResupplyShare));
        int shareId = dtoShare!.Roles.Single(r => r.Name == $"Pub-{t}").Id;
        await PostAsync(chef, "/api/regiments/members/role", new SetMemberRoleRequest(memberId, shareId));
        var okAll = await PostRawAsync(member, "/api/resupply", new CreateResupplyRequest($"All-{t}", "Deadlands", "", items, 1, "", ResupplyVisibility.Alliance));
        Assert.True(okAll.IsSuccessStatusCode);

        var chefList = await PostAsync<List<ResupplyRequestDto>>(chef, "/api/resupply",
            new CreateResupplyRequest($"DuChef-{t}", "Deadlands", "", items, 1, "", 0));
        string chefReqId = chefList!.Single(r => r.Title == $"DuChef-{t}").Id;
        var koDone = await PostRawAsync(member, "/api/resupply/done", new ResupplyActionRequest(chefReqId));
        Assert.Equal(HttpStatusCode.Forbidden, koDone.StatusCode);
        var koDel = await PostRawAsync(member, "/api/resupply/delete", new ResupplyActionRequest(chefReqId));
        Assert.Equal(HttpStatusCode.Forbidden, koDel.StatusCode);

        // + ResupplyManage : livrer la demande d'un autre devient possible.
        var dtoMan = await PostAsync<RegimentDto>(chef, "/api/regiments/roles",
            new CreateRoleRequest($"Gest-{t}", (int)(RegimentPermission.ResupplyShare | RegimentPermission.ResupplyManage)));
        int manId = dtoMan!.Roles.Single(r => r.Name == $"Gest-{t}").Id;
        await PostAsync(chef, "/api/regiments/members/role", new SetMemberRoleRequest(memberId, manId));
        var okDone = await PostRawAsync(member, "/api/resupply/done", new ResupplyActionRequest(chefReqId));
        Assert.True(okDone.IsSuccessStatusCode);
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
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("@everyone", "@everyone")]
    [InlineData("@here", "@here")]
    [InlineData("123456789012345678", "<@&123456789012345678>")]
    [InlineData("<@&123456789012345678>", "<@&123456789012345678>")]
    public void NormalizeRoleTag_accepte_les_formats_valides(string input, string expected) =>
        Assert.Equal(expected, FoxholeLogiHub.Api.Common.DiscordNotifier.NormalizeRoleTag(input));

    [Theory]
    [InlineData("@Logi")]
    [InlineData("<@123456789012345678>")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("<@&123456789012345678> sup")]
    public void NormalizeRoleTag_rejette_les_formats_invalides(string input) =>
        Assert.Null(FoxholeLogiHub.Api.Common.DiscordNotifier.NormalizeRoleTag(input));

    [Fact]
    public void Tagged_prefixe_seulement_si_mention()
    {
        Assert.Equal("msg", FoxholeLogiHub.Api.Common.DiscordNotifier.Tagged("", "msg"));
        Assert.Equal("<@&42424242> msg", FoxholeLogiHub.Api.Common.DiscordNotifier.Tagged("<@&42424242>", "msg"));
    }

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
