using System.Text.RegularExpressions;

namespace FoxholeLogiHub.Api.Auth;

/// <summary>
/// Implémente le strict nécessaire du flux « Sign in through Steam » (OpenID 2.0) :
/// construction de l'URL de connexion et vérification de l'assertion renvoyée par Steam.
/// </summary>
public static partial class SteamOpenId
{
    private const string SteamLogin = "https://steamcommunity.com/openid/login";

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(\d+)$")]
    private static partial Regex ClaimedIdRegex();

    /// <summary>URL vers laquelle rediriger le navigateur pour la connexion Steam.</summary>
    public static string BuildLoginUrl(string realm, string returnTo)
    {
        var p = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        };
        string query = string.Join("&", p.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{SteamLogin}?{query}";
    }

    /// <summary>
    /// Vérifie l'assertion OpenID renvoyée par Steam (en la repostant à Steam avec
    /// mode=check_authentication) et retourne le SteamID64 si elle est valide.
    /// </summary>
    public static async Task<string?> VerifyAsync(IQueryCollection query, HttpClient http)
    {
        var fields = query
            .Where(q => q.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(q => q.Key, q => q.Value.ToString());

        if (fields.Count == 0)
            return null;

        fields["openid.mode"] = "check_authentication";

        using var content = new FormUrlEncodedContent(fields);
        HttpResponseMessage resp = await http.PostAsync(SteamLogin, content);
        string body = await resp.Content.ReadAsStringAsync();

        if (!body.Contains("is_valid:true", StringComparison.Ordinal))
            return null;

        string claimedId = query["openid.claimed_id"].ToString();
        Match m = ClaimedIdRegex().Match(claimedId);
        return m.Success ? m.Groups[1].Value : null;
    }
}
