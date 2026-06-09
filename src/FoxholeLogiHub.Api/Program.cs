using System.Security.Claims;
using System.Text;
using FoxholeLogiHub.Api;
using FoxholeLogiHub.Api.Auth;
using FoxholeLogiHub.Api.Data;
using FoxholeLogiHub.Api.Presence;
using FoxholeLogiHub.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Port : Railway fournit PORT ; sinon 5080 en local.
string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Base de données : PostgreSQL en prod (DATABASE_URL), SQLite en local.
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
builder.Services.AddHttpClient();

// Authentification : JWT signé par le serveur, lié au Steam ID vérifié via Steam OpenID.
string jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? "dev-only-insecure-secret-change-me-0123456789abcdef";
builder.Services.AddSingleton(new TokenService(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            NameClaimType = TokenService.SteamIdClaim,
        };
        // SignalR ne peut pas envoyer d'en-tête Authorization : on lit le jeton en query (access_token).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                string? accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Railway met l'app derrière un proxy : on respecte X-Forwarded-Proto pour construire des URLs https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Schéma : en prod (Postgres) via migrations EF (Migrate) — les changements de modèle se
// déploient sans perte de données. En local (SQLite) via EnsureCreated (les migrations sont
// spécifiques à Postgres). RESET_DB=1 reste un filet de secours (vide la base).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    bool reset = Environment.GetEnvironmentVariable("RESET_DB") == "1";

    if (postgres is not null)
    {
        if (reset)
            await db.Database.EnsureDeletedAsync();
        await AdoptMigrationsIfLegacyAsync(db);
        await db.Database.MigrateAsync();
    }
    else
    {
        if (reset)
            db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "FoxholeLogiHub.Api", status = "ok" }));

// =================== Authentification Steam (OpenID) ===================

// Ouvre la connexion Steam ; redirect = URL loopback de l'app où renvoyer le jeton.
app.MapGet("/auth/steam/login", (HttpRequest request, string? redirect) =>
{
    if (string.IsNullOrEmpty(redirect) || !IsLoopback(redirect))
        return Results.BadRequest(new ApiError("Paramètre redirect (loopback) requis."));

    string baseUrl = $"{request.Scheme}://{request.Host}";
    string returnTo = $"{baseUrl}/auth/steam/callback?redirect={Uri.EscapeDataString(redirect)}";
    return Results.Redirect(SteamOpenId.BuildLoginUrl($"{baseUrl}/", returnTo));
});

// Steam renvoie ici : on vérifie l'assertion, on émet un JWT et on renvoie vers l'app (loopback).
app.MapGet("/auth/steam/callback", async (HttpRequest request, string? redirect, TokenService tokens, IHttpClientFactory httpFactory) =>
{
    if (string.IsNullOrEmpty(redirect) || !IsLoopback(redirect))
        return Results.BadRequest(new ApiError("redirect invalide."));

    string? steamId = await SteamOpenId.VerifyAsync(request.Query, httpFactory.CreateClient());
    if (steamId is null)
        return Results.BadRequest(new ApiError("Échec de la vérification Steam."));

    string token = tokens.Issue(steamId);
    string sep = redirect.Contains('?') ? "&" : "?";
    return Results.Redirect($"{redirect}{sep}token={Uri.EscapeDataString(token)}");
});

// =================== Utilisateurs ===================

app.MapPost("/api/users", async (UpsertUserRequest req, ClaimsPrincipal principal, AppDbContext db) =>
{
    string steamId = SteamId(principal);
    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    if (user is null)
    {
        user = new User { SteamId = steamId, FriendCode = await GenerateUniqueCodeAsync(db), CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
    }

    user.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? steamId : req.DisplayName.Trim();
    user.Faction = string.IsNullOrWhiteSpace(req.Faction) ? "Unknown" : req.Faction;
    user.LastSeenAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new UserDto(user.SteamId, user.DisplayName, user.Faction, user.FriendCode));
}).RequireAuthorization();

app.MapGet("/api/users/me", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    string steamId = SteamId(principal);
    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    return user is null
        ? Results.NotFound(new ApiError("Profil non créé."))
        : Results.Ok(new UserDto(user.SteamId, user.DisplayName, user.Faction, user.FriendCode));
}).RequireAuthorization();

// =================== Avatars ===================

app.MapPost("/api/users/avatar", async (HttpRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    string steamId = SteamId(principal);
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    byte[] bytes = ms.ToArray();
    if (bytes.Length == 0 || bytes.Length > 1_000_000)
        return Results.BadRequest(new ApiError("Avatar invalide (1 octet à 1 Mo)."));

    User? user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    if (user is null)
        return Results.NotFound(new ApiError("Profil non créé."));

    user.AvatarPng = bytes;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

// Lecture publique (les images sont chargées par URL dans l'app — peu sensibles).
app.MapGet("/api/users/{steamId}/avatar", async (string steamId, AppDbContext db) =>
{
    byte[]? png = await db.Users.Where(u => u.SteamId == steamId).Select(u => u.AvatarPng).FirstOrDefaultAsync();
    return png is null ? Results.NotFound() : Results.File(png, "image/png");
});

// =================== Amis ===================

app.MapPost("/api/friends/request", async (SendFriendRequestRequest req, ClaimsPrincipal principal, AppDbContext db, IHubContext<PresenceHub> hub) =>
{
    string meId = SteamId(principal);
    string code = FriendCodeGenerator.Normalize(req.FriendCode);
    if (string.IsNullOrWhiteSpace(code))
        return Results.BadRequest(new ApiError("Code d'ami requis."));

    User? me = await db.Users.FirstOrDefaultAsync(u => u.SteamId == meId);
    if (me is null)
        return Results.BadRequest(new ApiError("Profil non créé."));

    User? target = await db.Users.FirstOrDefaultAsync(u => u.FriendCode == code);
    if (target is null)
        return Results.NotFound(new ApiError("Aucun joueur avec ce code d'ami."));

    if (target.SteamId == meId)
        return Results.BadRequest(new ApiError("Tu ne peux pas t'ajouter toi-même."));

    bool already = await db.Friendships.AnyAsync(f => f.UserSteamId == meId && f.FriendSteamId == target.SteamId);
    if (already)
        return Results.BadRequest(new ApiError($"{target.DisplayName} est déjà dans tes amis."));

    var now = DateTimeOffset.UtcNow;

    FriendRequest? reverse = await db.FriendRequests
        .FirstOrDefaultAsync(r => r.FromSteamId == target.SteamId && r.ToSteamId == meId);
    if (reverse is not null)
    {
        db.FriendRequests.Remove(reverse);
        AddMutualFriendship(db, meId, target.SteamId, now);
        await db.SaveChangesAsync();
        await hub.Clients.User(target.SteamId).SendAsync(PresenceEvents.FriendsChanged);
        return Results.Ok(new SendFriendRequestResult(true, target.SteamId, target.DisplayName));
    }

    bool pending = await db.FriendRequests.AnyAsync(r => r.FromSteamId == meId && r.ToSteamId == target.SteamId);
    if (pending)
        return Results.BadRequest(new ApiError($"Demande déjà envoyée à {target.DisplayName}."));

    db.FriendRequests.Add(new FriendRequest { FromSteamId = meId, ToSteamId = target.SteamId, CreatedAt = now });
    await db.SaveChangesAsync();
    await hub.Clients.User(target.SteamId).SendAsync(PresenceEvents.FriendRequestReceived);
    return Results.Ok(new SendFriendRequestResult(false, target.SteamId, target.DisplayName));
}).RequireAuthorization();

app.MapPost("/api/friends/respond", async (RespondFriendRequestRequest req, ClaimsPrincipal principal, AppDbContext db, IHubContext<PresenceHub> hub) =>
{
    string meId = SteamId(principal);
    FriendRequest? request = await db.FriendRequests
        .FirstOrDefaultAsync(r => r.FromSteamId == req.RequesterSteamId && r.ToSteamId == meId);
    if (request is null)
        return Results.NotFound(new ApiError("Demande introuvable."));

    db.FriendRequests.Remove(request);
    if (req.Accept)
        AddMutualFriendship(db, meId, req.RequesterSteamId, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();

    if (req.Accept)
        await hub.Clients.User(req.RequesterSteamId).SendAsync(PresenceEvents.FriendsChanged);

    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/friends", async (ClaimsPrincipal principal, AppDbContext db, ConnectionTracker tracker) =>
{
    string steamId = SteamId(principal);
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
}).RequireAuthorization();

app.MapGet("/api/friends/requests", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    string steamId = SteamId(principal);
    var list = await (
        from r in db.FriendRequests
        join u in db.Users on r.FromSteamId equals u.SteamId
        where r.ToSteamId == steamId
        orderby r.Id
        select new FriendRequestDto(u.SteamId, u.DisplayName, u.Faction, u.AvatarPng != null)
    ).ToListAsync();
    return Results.Ok(list);
}).RequireAuthorization();

app.MapPost("/api/friends/remove", async (RemoveFriendRequest req, ClaimsPrincipal principal, AppDbContext db) =>
{
    string steamId = SteamId(principal);
    var rows = await db.Friendships
        .Where(f => (f.UserSteamId == steamId && f.FriendSteamId == req.FriendSteamId)
                 || (f.UserSteamId == req.FriendSteamId && f.FriendSteamId == steamId))
        .ToListAsync();
    db.Friendships.RemoveRange(rows);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

FoxholeLogiHub.Api.Regiments.RegimentEndpoints.MapRegimentEndpoints(app);
FoxholeLogiHub.Api.Stockpiles.StockpileEndpoints.MapStockpileEndpoints(app);

app.MapHub<PresenceHub>("/hub/presence");

app.Run();

// Adopte une base existante (créée jadis par EnsureCreated, sans historique de migrations)
// dans le système de migrations, SANS la recréer : on crée la table d'historique et on marque
// la migration initiale comme déjà appliquée. Migrate() n'appliquera alors que les suivantes.
static async Task AdoptMigrationsIfLegacyAsync(AppDbContext db)
{
    var history = db.GetService<IHistoryRepository>();
    if (await history.ExistsAsync())
        return; // déjà sous migrations

    bool legacySchema = await TableExistsAsync(db, "Users");
    if (!legacySchema)
        return; // base vierge → Migrate() créera tout

    await db.Database.ExecuteSqlRawAsync(history.GetCreateScript());
    string firstMigration = db.Database.GetMigrations().First();
    await db.Database.ExecuteSqlRawAsync(
        history.GetInsertScript(new HistoryRow(firstMigration, ProductInfo.GetVersion())));
}

static async Task<bool> TableExistsAsync(AppDbContext db, string table)
{
    List<int> rows = await db.Database
        .SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = {0}", table)
        .ToListAsync();
    return rows.FirstOrDefault() > 0;
}

static string SteamId(ClaimsPrincipal principal) =>
    principal.FindFirstValue(TokenService.SteamIdClaim)
    ?? throw new InvalidOperationException("Jeton sans Steam ID.");

static bool IsLoopback(string url) =>
    Uri.TryCreate(url, UriKind.Absolute, out Uri? u) &&
    (u.IsLoopback || u.Host is "localhost" or "127.0.0.1");

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
