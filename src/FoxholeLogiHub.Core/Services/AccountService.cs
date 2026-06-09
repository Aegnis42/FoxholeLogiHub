using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Steam;

namespace FoxholeLogiHub.Core.Services;

/// <summary>Résultat du chargement du compte courant.</summary>
public sealed record AccountResult(Account? Account, SteamProfile? Profile, PlayerSave? Save, string? Error);

/// <summary>
/// Orchestration du compte : identifie le joueur via son Steam ID (UserData.sav),
/// résout pseudo/avatar Steam et faction (.sav), et persiste le tout.
/// </summary>
public sealed class AccountService
{
    private readonly AccountStore _store = new();
    private readonly SteamProfileService _steam = new();
    private readonly PlayerSaveReader _saveReader = new();

    public AccountResult LoadOrCreate()
    {
        string? steamId = SaveGameLocator.GetSteamId();
        if (steamId is null)
            return new AccountResult(null, null, null,
                "Steam ID introuvable. Lance Foxhole au moins une fois (UserData.sav absent).");

        SteamProfile profile = _steam.Resolve(steamId);
        PlayerSave? save = _saveReader.ReadCurrentPlayer();

        Account account = _store.Load() ?? new Account
        {
            SteamId = steamId,
            DisplayName = profile.PersonaName ?? steamId,
            CreatedAt = DateTimeOffset.Now,
        };

        // Rafraîchit depuis les sources vivantes, sans écraser le pseudo choisi par l'utilisateur.
        account.SteamId = steamId;
        if (string.IsNullOrWhiteSpace(account.DisplayName))
            account.DisplayName = profile.PersonaName ?? steamId;
        if (save is not null)
            account.Faction = save.Faction;
        account.AvatarPath = profile.AvatarPath;

        _store.Save(account);
        return new AccountResult(account, profile, save, null);
    }

    public void UpdateDisplayName(Account account, string displayName)
    {
        account.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? account.SteamId
            : displayName.Trim();
        _store.Save(account);
    }
}
