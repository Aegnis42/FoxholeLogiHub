using FoxholeLogiHub.Api;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Port : Railway fournit PORT ; sinon 5080 en local.
string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Base de données :
//  - en prod (Railway) : PostgreSQL via la variable DATABASE_URL ;
//  - en local : SQLite (fichier foxhole.db).
string? postgres = TryGetPostgresConnectionString();
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (postgres is not null)
        o.UseNpgsql(postgres);
    else
        o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=foxhole.db");
});

builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddSingleton<IUserIdProvider, SteamIdUserIdProvider>();
builder.Services.AddSignalR();

var app = builder.Build();

// Crée la base si absente (MVP : pas de migrations EF).
// RESET_DB=1 : supprime puis recrée le schéma (à utiliser une fois après un changement de
// modèle, car EnsureCreated ne fait PAS évoluer une base existante). À retirer ensuite.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (Environment.GetEnvironmentVariable("RESET_DB") == "1")
        db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
}

app.MapGet("/", () => Results.Ok(new { service = "FoxholeLogiHub.Api", status = "ok" }));

// --- Utilisateurs ---

app.MapPost("/api/users", async (UpsertUserRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.SteamId))
        return Results.BadRequest(new ApiError("SteamId requis."));

    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == req.SteamId);
    if (user is null)
    {
        user = new User
        {
            SteamId = req.SteamId,
            FriendCode = await GenerateUniqueCodeAsync(db),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
    }

    user.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.SteamId : req.DisplayName.Trim();
    user.Faction = string.IsNullOrWhiteSpace(req.Faction) ? "Unknown" : req.Faction;
    user.LastSeenAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new UserDto(user.SteamId, user.DisplayName, user.Faction, user.FriendCode));
});

app.MapGet("/api/users/{steamId}", async (string steamId, AppDbContext db) =>
{
    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    return user is null
        ? Results.NotFound(new ApiError("Utilisateur inconnu."))
        : Results.Ok(new UserDto(user.SteamId, user.DisplayName, user.Faction, user.FriendCode));
});

// --- Avatars (partagés via le serveur) ---

app.MapPost("/api/users/{steamId}/avatar", async (string steamId, HttpRequest request, AppDbContext db) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    byte[] bytes = ms.ToArray();
    if (bytes.Length == 0 || bytes.Length > 1_000_000)
        return Results.BadRequest(new ApiError("Avatar invalide (1 octet à 1 Mo)."));

    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    if (user is null)
        return Results.NotFound(new ApiError("Utilisateur inconnu."));

    user.AvatarPng = bytes;
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/api/users/{steamId}/avatar", async (string steamId, AppDbContext db) =>
{
    byte[]? png = await db.Users
        .Where(u => u.SteamId == steamId)
        .Select(u => u.AvatarPng)
        .FirstOrDefaultAsync();
    return png is null ? Results.NotFound() : Results.File(png, "image/png");
});

// --- Amis ---

// Envoyer une demande d'ami par code.
app.MapPost("/api/friends/request", async (SendFriendRequestRequest req, AppDbContext db, IHubContext<PresenceHub> hub) =>
{
    string code = FriendCodeGenerator.Normalize(req.FriendCode);
    if (string.IsNullOrWhiteSpace(req.SteamId) || string.IsNullOrWhiteSpace(code))
        return Results.BadRequest(new ApiError("SteamId et code d'ami requis."));

    User? me = await db.Users.FirstOrDefaultAsync(u => u.SteamId == req.SteamId);
    if (me is null)
        return Results.BadRequest(new ApiError("Ton compte n'existe pas encore côté serveur."));

    User? target = await db.Users.FirstOrDefaultAsync(u => u.FriendCode == code);
    if (target is null)
        return Results.NotFound(new ApiError("Aucun joueur avec ce code d'ami."));

    if (target.SteamId == me.SteamId)
        return Results.BadRequest(new ApiError("Tu ne peux pas t'ajouter toi-même."));

    bool already = await db.Friendships.AnyAsync(f =>
        f.UserSteamId == me.SteamId && f.FriendSteamId == target.SteamId);
    if (already)
        return Results.BadRequest(new ApiError($"{target.DisplayName} est déjà dans tes amis."));

    var now = DateTimeOffset.UtcNow;

    // Si la cible nous avait déjà envoyé une demande → amitié directe.
    FriendRequest? reverse = await db.FriendRequests
        .FirstOrDefaultAsync(r => r.FromSteamId == target.SteamId && r.ToSteamId == me.SteamId);
    if (reverse is not null)
    {
        db.FriendRequests.Remove(reverse);
        AddMutualFriendship(db, me.SteamId, target.SteamId, now);
        await db.SaveChangesAsync();
        await hub.Clients.User(target.SteamId).SendAsync(PresenceEvents.FriendsChanged);
        return Results.Ok(new SendFriendRequestResult(true, target.SteamId, target.DisplayName));
    }

    bool pending = await db.FriendRequests.AnyAsync(r => r.FromSteamId == me.SteamId && r.ToSteamId == target.SteamId);
    if (pending)
        return Results.BadRequest(new ApiError($"Demande déjà envoyée à {target.DisplayName}."));

    db.FriendRequests.Add(new FriendRequest { FromSteamId = me.SteamId, ToSteamId = target.SteamId, CreatedAt = now });
    await db.SaveChangesAsync();
    await hub.Clients.User(target.SteamId).SendAsync(PresenceEvents.FriendRequestReceived);
    return Results.Ok(new SendFriendRequestResult(false, target.SteamId, target.DisplayName));
});

// Répondre à une demande reçue (accepter / refuser).
app.MapPost("/api/friends/respond", async (RespondFriendRequestRequest req, AppDbContext db, IHubContext<PresenceHub> hub) =>
{
    FriendRequest? request = await db.FriendRequests
        .FirstOrDefaultAsync(r => r.FromSteamId == req.RequesterSteamId && r.ToSteamId == req.SteamId);
    if (request is null)
        return Results.NotFound(new ApiError("Demande introuvable."));

    db.FriendRequests.Remove(request);
    if (req.Accept)
        AddMutualFriendship(db, req.SteamId, req.RequesterSteamId, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();

    if (req.Accept)
        await hub.Clients.User(req.RequesterSteamId).SendAsync(PresenceEvents.FriendsChanged);

    return Results.NoContent();
});

// Demandes d'ami reçues (en attente).
app.MapGet("/api/friends/requests/{steamId}", async (string steamId, AppDbContext db) =>
{
    var list = await (
        from r in db.FriendRequests
        join u in db.Users on r.FromSteamId equals u.SteamId
        where r.ToSteamId == steamId
        orderby r.Id
        select new FriendRequestDto(u.SteamId, u.DisplayName, u.Faction, u.AvatarPng != null)
    ).ToListAsync();
    return Results.Ok(list);
});

app.MapGet("/api/friends/{steamId}", async (string steamId, AppDbContext db, ConnectionTracker tracker) =>
{
    var friends = await (
        from f in db.Friendships
        join u in db.Users on f.FriendSteamId equals u.SteamId
        where f.UserSteamId == steamId
        select new { u.SteamId, u.DisplayName, u.Faction, HasAvatar = u.AvatarPng != null }
    ).ToListAsync();

    List<FriendDto> result = friends
        .Select(x => new FriendDto(x.SteamId, x.DisplayName, x.Faction, tracker.IsOnline(x.SteamId), x.HasAvatar))
        .OrderByDescending(f => f.Online)
        .ThenBy(f => f.DisplayName)
        .ToList();

    return Results.Ok(result);
});

app.MapPost("/api/friends/remove", async (RemoveFriendRequest req, AppDbContext db) =>
{
    var rows = await db.Friendships
        .Where(f => (f.UserSteamId == req.SteamId && f.FriendSteamId == req.FriendSteamId)
                 || (f.UserSteamId == req.FriendSteamId && f.FriendSteamId == req.SteamId))
        .ToListAsync();
    db.Friendships.RemoveRange(rows);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapHub<PresenceHub>("/hub/presence");

app.Run();

static async Task<string> GenerateUniqueCodeAsync(AppDbContext db)
{
    for (int attempt = 0; attempt < 20; attempt++)
    {
        string code = FriendCodeGenerator.Generate();
        if (!await db.Users.AnyAsync(u => u.FriendCode == code))
            return code;
    }
    throw new InvalidOperationException("Impossible de générer un code d'ami unique.");
}

static void AddMutualFriendship(AppDbContext db, string a, string b, DateTimeOffset now)
{
    db.Friendships.Add(new Friendship { UserSteamId = a, FriendSteamId = b, CreatedAt = now });
    db.Friendships.Add(new Friendship { UserSteamId = b, FriendSteamId = a, CreatedAt = now });
}

// Convertit le DATABASE_URL de Railway (postgresql://user:pass@host:port/db) en
// chaîne de connexion Npgsql. Retourne null si la variable est absente (→ SQLite local).
static string? TryGetPostgresConnectionString()
{
    string? url = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(url))
        return null;

    var uri = new Uri(url);
    string[] userInfo = uri.UserInfo.Split(':', 2);

    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        Database = uri.AbsolutePath.TrimStart('/'),
        SslMode = SslMode.Prefer,
    };
    return csb.ConnectionString;
}
