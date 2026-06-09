using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FoxholeLogiHub.Api.Auth;

/// <summary>Émet et signe les jetons JWT liés au Steam ID vérifié.</summary>
public sealed class TokenService
{
    /// <summary>Type du claim portant le SteamID64 (explicite pour éviter le mapping JWT).</summary>
    public const string SteamIdClaim = "steamid";

    public SymmetricSecurityKey Key { get; }

    public TokenService(string secret) =>
        Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

    public string Issue(string steamId)
    {
        var token = new JwtSecurityToken(
            claims: new[] { new Claim(SteamIdClaim, steamId) },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
