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
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
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

// --- Amis ---

app.MapPost("/api/friends/add", async (AddFriendRequest req, AppDbContext db, ConnectionTracker tracker) =>
{
    string code = FriendCodeGenerator.Normalize(req.FriendCode);
    if (string.IsNullOrWhiteSpace(req.SteamId) || string.IsNullOrWhiteSpace(code))
        return Results.BadRequest(new ApiError("SteamId et code d'ami requis."));

    User? me = await db.Users.FirstOrDefaultAsync(u => u.SteamId == req.SteamId);
    if (me is null)
        return Results.BadRequest(new ApiError("Ton compte n'existe pas encore côté serveur."));

    User? friend = await db.Users.FirstOrDefaultAsync(u => u.FriendCode == code);
    if (friend is null)
        return Results.NotFound(new ApiError("Aucun joueur avec ce code d'ami."));

    if (friend.SteamId == me.SteamId)
        return Results.BadRequest(new ApiError("Tu ne peux pas t'ajouter toi-même."));

    bool already = await db.Friendships.AnyAsync(f =>
        f.UserSteamId == me.SteamId && f.FriendSteamId == friend.SteamId);
    if (already)
        return Results.BadRequest(new ApiError($"{friend.DisplayName} est déjà dans tes amis."));

    var now = DateTimeOffset.UtcNow;
    db.Friendships.Add(new Friendship { UserSteamId = me.SteamId, FriendSteamId = friend.SteamId, CreatedAt = now });
    db.Friendships.Add(new Friendship { UserSteamId = friend.SteamId, FriendSteamId = me.SteamId, CreatedAt = now });
    await db.SaveChangesAsync();

    return Results.Ok(new FriendDto(friend.SteamId, friend.DisplayName, friend.Faction, tracker.IsOnline(friend.SteamId)));
});

app.MapGet("/api/friends/{steamId}", async (string steamId, AppDbContext db, ConnectionTracker tracker) =>
{
    var friends = await (
        from f in db.Friendships
        join u in db.Users on f.FriendSteamId equals u.SteamId
        where f.UserSteamId == steamId
        select new { u.SteamId, u.DisplayName, u.Faction }
    ).ToListAsync();

    List<FriendDto> result = friends
        .Select(x => new FriendDto(x.SteamId, x.DisplayName, x.Faction, tracker.IsOnline(x.SteamId)))
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
