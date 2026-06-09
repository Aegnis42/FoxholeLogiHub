using FoxholeLogiHub.Core.Services;
using FoxholeLogiHub.Core.Steam;
using Xunit;
using Xunit.Abstractions;

namespace FoxholeLogiHub.Core.Tests;

public sealed class AccountServiceTests
{
    private readonly ITestOutputHelper _output;
    public AccountServiceTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void Resolves_steam_profile_locally()
    {
        string? steamId = SaveGameLocator.GetSteamId();
        Skip.If(steamId is null, "Pas de Steam ID (UserData.sav absent).");

        SteamProfile profile = new SteamProfileService().Resolve(steamId!);

        _output.WriteLine($"SteamId     : {profile.SteamId}");
        _output.WriteLine($"PersonaName : {profile.PersonaName}");
        _output.WriteLine($"AccountName : {profile.AccountName}");
        _output.WriteLine($"AvatarPath  : {profile.AvatarPath}");

        Assert.Equal(steamId, profile.SteamId);
        // Le pseudo et l'avatar doivent être trouvés si Steam est installé sur cette machine.
        Skip.If(profile.PersonaName is null, "loginusers.vdf introuvable sur cette machine.");
        Assert.False(string.IsNullOrWhiteSpace(profile.PersonaName));
    }

    [SkippableFact]
    public void Loads_or_creates_account()
    {
        var result = new AccountService().LoadOrCreate();
        Skip.If(result.Error is not null, result.Error);

        _output.WriteLine($"DisplayName : {result.Account!.DisplayName}");
        _output.WriteLine($"Faction     : {result.Account.Faction}");
        _output.WriteLine($"Avatar      : {result.Account.AvatarPath}");

        Assert.False(string.IsNullOrWhiteSpace(result.Account.DisplayName));
        Assert.Equal(result.Account.SteamId, SaveGameLocator.GetSteamId());
    }
}
