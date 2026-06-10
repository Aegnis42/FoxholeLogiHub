using System.Net;
using FoxholeLogiHub.Contracts;
using static FoxholeLogiHub.Api.Tests.TestWorld;

namespace FoxholeLogiHub.Api.Tests;

/// <summary>
/// Verrouille les règles métier du ravitaillement : matrice de visibilité (privé/alliance/public)
/// et permissions d'action (claim/done/reopen) — y compris contre le griefing inter-régiment.
/// Chaque test construit son propre monde (Steam IDs uniques) ; les titres sont suffixés d'un tag
/// unique car les demandes PUBLIQUES des autres tests sont visibles de tous.
/// </summary>
public sealed class ResupplyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ResupplyTests(ApiFactory factory) => _factory = factory;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static CreateResupplyRequest Req(string title, int visibility) => new(
        title, "Deadlands", "A1",
        new List<ResupplyItemDto> { new("Bandages", "Bandages", "Médical", 100) },
        ResupplyPriority.Normal, "", visibility);

    [Fact]
    public async Task Visibilite_privee_alliance_publique()
    {
        var (a, regA) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var (b, regB) = await UserWithRegimentAsync(_factory, "Bravo", "BRV");
        var (c, _) = await UserWithRegimentAsync(_factory, "Charlie", "CHA");
        await AllyAsync(a, regA, b, regB);
        string t = Tag();

        await PostAsync(a, "/api/resupply", Req($"PRIV-{t}", ResupplyVisibility.Regiment));
        await PostAsync(a, "/api/resupply", Req($"ALLI-{t}", ResupplyVisibility.Alliance));
        await PostAsync(a, "/api/resupply", Req($"PUBL-{t}", ResupplyVisibility.Public));

        var aList = await GetAsync<List<ResupplyRequestDto>>(a, "/api/resupply");
        Assert.Contains(aList, r => r.Title == $"PRIV-{t}");
        Assert.Contains(aList, r => r.Title == $"ALLI-{t}");
        Assert.Contains(aList, r => r.Title == $"PUBL-{t}");

        var bList = await GetAsync<List<ResupplyRequestDto>>(b, "/api/resupply");
        Assert.DoesNotContain(bList, r => r.Title == $"PRIV-{t}");
        Assert.Contains(bList, r => r.Title == $"ALLI-{t}");
        Assert.Contains(bList, r => r.Title == $"PUBL-{t}");

        var cList = await GetAsync<List<ResupplyRequestDto>>(c, "/api/resupply");
        Assert.DoesNotContain(cList, r => r.Title == $"PRIV-{t}");
        Assert.DoesNotContain(cList, r => r.Title == $"ALLI-{t}");
        Assert.Contains(cList, r => r.Title == $"PUBL-{t}");
    }

    [Fact]
    public async Task Livree_refusee_a_un_inconnu_autorisee_au_preneur_et_au_proprietaire()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var (c, _) = await UserWithRegimentAsync(_factory, "Charlie", "CHA");
        string t = Tag();

        await PostAsync(a, "/api/resupply", Req($"PUBL-{t}", ResupplyVisibility.Public));
        var cList = await GetAsync<List<ResupplyRequestDto>>(c, "/api/resupply");
        string id = cList.Single(r => r.Title == $"PUBL-{t}").Id;

        // Un inconnu (ni propriétaire ni preneur) ne peut pas marquer "livrée".
        var forbidden = await PostRawAsync(c, "/api/resupply/done", new ResupplyActionRequest(id));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Mais il peut la prendre en charge, puis la livrer.
        await PostAsync(c, "/api/resupply/claim", new ResupplyActionRequest(id));
        await PostAsync(c, "/api/resupply/done", new ResupplyActionRequest(id));

        // Et le régiment propriétaire peut la rouvrir.
        await PostAsync(a, "/api/resupply/reopen", new ResupplyActionRequest(id));
        var aList = await GetAsync<List<ResupplyRequestDto>>(a, "/api/resupply");
        Assert.NotEqual(ResupplyStatus.Done, aList.Single(r => r.Title == $"PUBL-{t}").Status);
    }

    [Fact]
    public async Task Vol_de_prise_en_charge_refuse_aux_etrangers_autorise_au_proprietaire()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var (b, _) = await UserWithRegimentAsync(_factory, "Bravo", "BRV");
        var (c, _) = await UserWithRegimentAsync(_factory, "Charlie", "CHA");
        string t = Tag();

        await PostAsync(a, "/api/resupply", Req($"PUBL-{t}", ResupplyVisibility.Public));
        var bList = await GetAsync<List<ResupplyRequestDto>>(b, "/api/resupply");
        string id = bList.Single(r => r.Title == $"PUBL-{t}").Id;

        await PostAsync(b, "/api/resupply/claim", new ResupplyActionRequest(id)); // B la prend

        // C (étranger) ne peut pas la voler à B.
        var refused = await PostRawAsync(c, "/api/resupply/claim", new ResupplyActionRequest(id));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // A (régiment propriétaire) peut la réattribuer.
        var afterA = await PostAsync<List<ResupplyRequestDto>>(a, "/api/resupply/claim", new ResupplyActionRequest(id));
        Assert.True(afterA.Single(r => r.Title == $"PUBL-{t}").MineClaim);
    }

    [Fact]
    public async Task Suppression_reservee_au_regiment_proprietaire()
    {
        var (a, _) = await UserWithRegimentAsync(_factory, "Alpha", "ALP");
        var (c, _) = await UserWithRegimentAsync(_factory, "Charlie", "CHA");
        string t = Tag();

        await PostAsync(a, "/api/resupply", Req($"PUBL-{t}", ResupplyVisibility.Public));
        var cList = await GetAsync<List<ResupplyRequestDto>>(c, "/api/resupply");
        string id = cList.Single(r => r.Title == $"PUBL-{t}").Id;

        var refused = await PostRawAsync(c, "/api/resupply/delete", new ResupplyActionRequest(id));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var afterA = await PostAsync<List<ResupplyRequestDto>>(a, "/api/resupply/delete", new ResupplyActionRequest(id));
        Assert.DoesNotContain(afterA, r => r.Title == $"PUBL-{t}");
    }

    [Fact]
    public async Task Anonyme_rejete()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/resupply");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
