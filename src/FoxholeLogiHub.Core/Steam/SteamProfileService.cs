namespace FoxholeLogiHub.Core.Steam;

/// <summary>Profil Steam résolu localement (pseudo + avatar mis en cache par le client Steam).</summary>
public sealed class SteamProfile
{
    public required string SteamId { get; init; }
    public string? PersonaName { get; init; }
    public string? AccountName { get; init; }
    /// <summary>Chemin local du PNG d'avatar (cache Steam), ou null s'il n'est pas en cache.</summary>
    public string? AvatarPath { get; init; }
}

/// <summary>
/// Récupère le pseudo et l'avatar Steam d'un joueur **100 % hors-ligne** depuis les fichiers
/// locaux du client Steam : <c>config/loginusers.vdf</c> et <c>config/avatarcache/&lt;steamid&gt;.png</c>.
/// </summary>
public sealed class SteamProfileService
{
    public SteamProfile Resolve(string steamId)
    {
        string? steamPath = SteamLocator.GetSteamPath();
        string? persona = null;
        string? account = null;
        string? avatar = null;

        if (steamPath is not null)
        {
            (persona, account) = ReadLoginUser(steamPath, steamId);

            string avatarPng = Path.Combine(steamPath, "config", "avatarcache", steamId + ".png");
            if (File.Exists(avatarPng))
                avatar = avatarPng;
        }

        return new SteamProfile
        {
            SteamId = steamId,
            PersonaName = persona,
            AccountName = account,
            AvatarPath = avatar,
        };
    }

    private static (string? persona, string? account) ReadLoginUser(string steamPath, string steamId)
    {
        string vdf = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (!File.Exists(vdf))
            return (null, null);

        try
        {
            VdfNode root = VdfParser.Parse(File.ReadAllText(vdf));
            VdfNode? user = root["users"]?[steamId];
            return (user?["PersonaName"]?.Value, user?["AccountName"]?.Value);
        }
        catch
        {
            return (null, null);
        }
    }
}
