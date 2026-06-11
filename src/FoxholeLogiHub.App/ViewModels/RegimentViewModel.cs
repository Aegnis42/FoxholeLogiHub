using System.Collections.ObjectModel;
using System.Windows;
using FoxholeLogiHub.App.Services;
using FoxholeLogiHub.Contracts;
using FoxholeLogiHub.Core.Models;
using FoxholeLogiHub.Core.Services;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Gestion du régiment : création/adhésion, membres, rôles/permissions, invitations, alliances.</summary>
public sealed class RegimentViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TokenStore _tokenStore = new();

    private Account? _account;
    private FriendsViewModel? _friends;
    private RegimentClient? _client;

    private bool _authed;
    private bool _hasRegiment;
    private bool _busy;
    private string _status = "";

    private string _name = "—";
    private string _tag = "";
    private string _faction = "";
    private string _inviteCode = "—";
    private bool _iAmOwner;
    private int _myPermissions;

    private string _newName = "";
    private string _newTag = "";
    private string _joinCode = "";
    private string _allianceCode = "";
    private string _newRoleName = "";

    public ObservableCollection<RegimentMemberItemViewModel> Members { get; } = new();
    public ObservableCollection<RegimentRoleItemViewModel> Roles { get; } = new();
    public ObservableCollection<RegimentAllianceItemViewModel> Alliances { get; } = new();
    public ObservableCollection<RegimentInviteItemViewModel> Invites { get; } = new();

    /// <summary>Amis (depuis le module amis) pour les inviter au régiment.</summary>
    public ObservableCollection<FriendItemViewModel>? Friends => _friends?.Friends;

    public bool Authed { get => _authed; private set { Set(ref _authed, value); RaiseViewFlags(); } }
    public bool HasRegiment { get => _hasRegiment; private set { Set(ref _hasRegiment, value); RaiseViewFlags(); } }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool ShowAuthNeeded => !Authed;
    public bool ShowNoRegiment => Authed && !HasRegiment;
    public bool ShowRegiment => Authed && HasRegiment;
    public bool HasInvites => Invites.Count > 0;

    private void RaiseViewFlags()
    {
        Raise(nameof(ShowAuthNeeded));
        Raise(nameof(ShowNoRegiment));
        Raise(nameof(ShowRegiment));
    }

    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Tag { get => _tag; private set => Set(ref _tag, value); }
    public string Faction { get => _faction; private set => Set(ref _faction, value); }
    public string InviteCode { get => _inviteCode; private set => Set(ref _inviteCode, value); }
    public bool IAmOwner { get => _iAmOwner; private set => Set(ref _iAmOwner, value); }

    public int MyPermissions
    {
        get => _myPermissions;
        private set
        {
            Set(ref _myPermissions, value);
            Raise(nameof(CanInvite)); Raise(nameof(CanManageMembers)); Raise(nameof(CanManageRoles));
            Raise(nameof(CanManageRegiment)); Raise(nameof(CanManageAlliances)); Raise(nameof(CanManageStockpiles));
        }
    }

    private bool Has(RegimentPermission p) => ((RegimentPermission)MyPermissions & p) == p;
    public bool CanInvite => Has(RegimentPermission.Invite);
    public bool CanManageMembers => Has(RegimentPermission.ManageMembers);
    public bool CanManageRoles => Has(RegimentPermission.ManageRoles);
    public bool CanManageRegiment => Has(RegimentPermission.ManageRegiment);
    public bool CanManageAlliances => Has(RegimentPermission.ManageAlliances);
    public bool CanManageStockpiles => Has(RegimentPermission.ManageStockpiles);

    /// <summary>Régiments alliés (alliances acceptées) — pour le partage de stockpiles.</summary>
    public IReadOnlyList<(string Id, string Name)> AcceptedAllies =>
        Alliances.Where(a => a.IsAccepted).Select(a => (a.RegimentId, a.Name)).ToList();

    public string NewName { get => _newName; set => Set(ref _newName, value); }
    public string NewTag { get => _newTag; set => Set(ref _newTag, value); }
    public string JoinCode { get => _joinCode; set => Set(ref _joinCode, value); }
    public string AllianceCode { get => _allianceCode; set => Set(ref _allianceCode, value); }
    public string NewRoleName { get => _newRoleName; set => Set(ref _newRoleName, value); }

    public void Initialize(Account account, FriendsViewModel friends)
    {
        _account = account;
        _friends = friends;
        Raise(nameof(Friends));
    }

    public void ClearAuth()
    {
        _client?.Dispose();
        _client = null;
        Authed = false;
        HasRegiment = false;
        Members.Clear(); Roles.Clear(); Alliances.Clear(); Invites.Clear();
        Raise(nameof(HasInvites));
        Status = "Connecte-toi avec Steam (onglet Amis).";
    }

    /// <summary>(Re)charge le régiment et les invitations avec le jeton courant.</summary>
    public async Task RefreshAsync()
    {
        string? token = _tokenStore.Load();
        if (_account is null || token is null)
        {
            ClearAuth();
            return;
        }

        Busy = true;
        try
        {
            _client?.Dispose();
            _client = new RegimentClient(_settingsStore.Load().ApiBaseUrl, token);
            Authed = true;

            RegimentDto? reg = await _client.GetMineAsync();
            Apply(reg);
            await ReloadInvitesAsync();
            Status = HasRegiment ? $"Régiment : {Name}" : "Tu n'es dans aucun régiment.";
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task ReloadInvitesAsync()
    {
        if (_client is null)
            return;
        try
        {
            List<RegimentInviteDto> list = await _client.GetInvitesAsync();
            Invites.Clear();
            foreach (var i in list)
                Invites.Add(new RegimentInviteItemViewModel(i));
            Raise(nameof(HasInvites));
        }
        catch { /* ignore */ }
    }

    private void Apply(RegimentDto? reg)
    {
        if (reg is null)
        {
            HasRegiment = false;
            Members.Clear(); Roles.Clear(); Alliances.Clear();
            return;
        }

        Name = reg.Name;
        Tag = reg.Tag;
        Faction = reg.Faction;
        InviteCode = Format(reg.InviteCode);
        IAmOwner = reg.IAmOwner;
        MyPermissions = reg.MyPermissions;

        Members.Clear();
        foreach (var m in reg.Members)
            Members.Add(new RegimentMemberItemViewModel(m, _client!.AvatarUrl(m.SteamId), reg.Roles, CanManageMembers));

        Roles.Clear();
        foreach (var r in reg.Roles)
            Roles.Add(new RegimentRoleItemViewModel(r, CanManageRoles));

        Alliances.Clear();
        foreach (var a in reg.Alliances)
            Alliances.Add(new RegimentAllianceItemViewModel(a));

        HasRegiment = true;

        if (reg.IAmOwner)
            _ = LoadWebhookAsync(); // meilleur effort, ne bloque pas le refresh
    }

    // ---------- Webhook Discord (chef) ----------

    private string _webhookInput = "";
    public string WebhookInput { get => _webhookInput; set => Set(ref _webhookInput, value); }
    private string _webhookTagInput = "";
    public string WebhookTagInput { get => _webhookTagInput; set => Set(ref _webhookTagInput, value); }
    private string _webhookState = "";
    public string WebhookState { get => _webhookState; private set => Set(ref _webhookState, value); }

    private void ApplyWebhookDto(RegimentWebhookDto? dto, string suffix = "")
    {
        WebhookTagInput = dto?.RoleTag ?? "";
        WebhookState = dto is { Configured: true }
            ? $"Connecté : {dto.Masked}"
              + (string.IsNullOrEmpty(dto.RoleTag) ? "" : $" · mention {dto.RoleTag}")
              + suffix
            : "Aucun webhook configuré.";
    }

    private async Task LoadWebhookAsync()
    {
        if (_client is null)
            return;
        try
        {
            ApplyWebhookDto(await _client.GetWebhookAsync());
        }
        catch { /* meilleur effort */ }
    }

    /// <summary>
    /// Enregistre le webhook et/ou la mention. URL vide + mention fournie = ne change que la
    /// mention (l'URL enregistrée est masquée, pas besoin de la recoller).
    /// </summary>
    public async Task SaveWebhookAsync()
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            var dto = await _client.SetWebhookAsync(WebhookInput.Trim(), WebhookTagInput.Trim());
            WebhookInput = "";
            ApplyWebhookDto(dto, " — message de test envoyé ✅");
            Status = "Webhook Discord mis à jour.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    /// <summary>Coupe le webhook (et sa mention).</summary>
    public async Task DisableWebhookAsync()
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            ApplyWebhookDto(await _client.SetWebhookAsync("", null));
            WebhookInput = "";
            Status = "Webhook Discord désactivé.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task RunAsync(Func<Task<RegimentDto?>> action, string okMessage)
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            RegimentDto? reg = await action();
            Apply(reg);
            Status = okMessage;
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task RunVoidAsync(Func<Task> action, string okMessage)
    {
        if (_client is null || Busy)
            return;
        Busy = true;
        try
        {
            await action();
            await RefreshSilentAsync();
            Status = okMessage;
        }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (FriendException fex) { Status = fex.Message; }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task RefreshSilentAsync()
    {
        if (_client is null) return;
        Apply(await _client.GetMineAsync());
        await ReloadInvitesAsync();
    }

    // --- Actions ---

    public async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Donne un nom au régiment."; return; }
        string faction = _account?.Faction.ToString() ?? "Unknown";
        await RunAsync(() => _client!.CreateAsync(NewName.Trim(), NewTag.Trim(), faction), "Régiment créé.");
        NewName = ""; NewTag = "";
    }

    public async Task JoinAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinCode)) return;
        await RunAsync(() => _client!.JoinAsync(JoinCode.Trim()), "Régiment rejoint.");
        JoinCode = "";
    }

    public Task LeaveAsync() => RunVoidAsync(() => _client!.LeaveAsync(), "Tu as quitté le régiment.");
    public Task DeleteAsync() => RunVoidAsync(() => _client!.DeleteAsync(), "Régiment supprimé.");
    public Task RegenerateCodeAsync() => RunVoidAsync(() => _client!.RegenerateCodeAsync(), "Nouveau code généré.");
    public Task WarResetAsync() => RunVoidAsync(() => _client!.WarResetAsync(), "Fin de guerre : stockpiles et demandes réinitialisés.");

    public async Task CreateRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRoleName)) return;
        await RunAsync(() => _client!.CreateRoleAsync(NewRoleName.Trim(), 0), $"Rôle « {NewRoleName.Trim()} » créé.");
        NewRoleName = "";
    }

    public Task SaveRoleAsync(RegimentRoleItemViewModel role) =>
        RunAsync(() => _client!.UpdateRoleAsync(role.Id, role.Name.Trim(), role.ComposePermissions()), $"Rôle « {role.Name} » enregistré.");

    public Task DeleteRoleAsync(RegimentRoleItemViewModel role) =>
        RunAsync(() => _client!.DeleteRoleAsync(role.Id), "Rôle supprimé.");

    public Task SetMemberRoleAsync(string steamId, int roleId) =>
        RunAsync(() => _client!.SetMemberRoleAsync(steamId, roleId), "Rôle du membre mis à jour.");

    public Task KickAsync(RegimentMemberItemViewModel member) =>
        RunAsync(() => _client!.KickAsync(member.SteamId), $"{member.DisplayName} exclu.");

    public Task InviteFriendAsync(string friendSteamId, string friendName) =>
        RunVoidAsync(() => _client!.InviteFriendAsync(friendSteamId), $"{friendName} invité.");

    public async Task RespondInviteAsync(RegimentInviteItemViewModel invite, bool accept)
    {
        if (_client is null || Busy) return;
        Busy = true;
        try
        {
            await _client.RespondInviteAsync(invite.RegimentId, accept);
            Invites.Remove(invite);
            Raise(nameof(HasInvites));
            if (accept) await RefreshSilentAsync();
            Status = accept ? $"Tu as rejoint {invite.Name}." : "Invitation refusée.";
        }
        catch (FriendException fex) { Status = fex.Message; }
        catch (AuthRequiredException) { ClearAuth(); }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { Busy = false; }
    }

    public async Task ProposeAllianceAsync()
    {
        if (string.IsNullOrWhiteSpace(AllianceCode)) return;
        await RunVoidAsync(() => _client!.ProposeAllianceAsync(AllianceCode.Trim()), "Demande d'alliance envoyée.");
        AllianceCode = "";
    }

    public Task RespondAllianceAsync(RegimentAllianceItemViewModel a, bool accept) =>
        RunVoidAsync(() => _client!.RespondAllianceAsync(a.RegimentId, accept), accept ? $"Alliance avec {a.Name} acceptée." : "Demande refusée.");

    public Task RemoveAllianceAsync(RegimentAllianceItemViewModel a) =>
        RunVoidAsync(() => _client!.RemoveAllianceAsync(a.RegimentId), "Alliance rompue.");

    private static string Format(string code) =>
        code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;
}
