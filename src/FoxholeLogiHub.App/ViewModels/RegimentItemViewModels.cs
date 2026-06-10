using System.Collections.Generic;
using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Un membre du régiment (avec gestion de rôle/exclusion selon les permissions).</summary>
public sealed class RegimentMemberItemViewModel : ObservableObject
{
    public RegimentMemberItemViewModel(RegimentMemberDto dto, string? avatarUrl, IReadOnlyList<RegimentRoleDto> roles, bool canManage)
    {
        SteamId = dto.SteamId;
        DisplayName = dto.DisplayName;
        Faction = dto.Faction;
        Online = dto.Online;
        RoleId = dto.RoleId;
        RoleName = dto.RoleName;
        IsOwner = dto.IsOwner;
        AvatarUrl = dto.HasAvatar ? avatarUrl : null;
        Roles = roles;
        CanManage = canManage && !dto.IsOwner;
    }

    public string SteamId { get; }
    public string DisplayName { get; }
    public string Faction { get; }
    public bool Online { get; }
    public int RoleId { get; }
    public string RoleName { get; }
    public bool IsOwner { get; }
    public string? AvatarUrl { get; }
    public IReadOnlyList<RegimentRoleDto> Roles { get; }
    public bool CanManage { get; }

    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();
    public Brush FactionBrush => FactionColors.For(Faction);
    public Brush StatusBrush => Online
        ? Palette.Online
        : Palette.Offline;
    public string RoleBadge => IsOwner ? $"{RoleName} ★" : RoleName;
}

/// <summary>Un rôle, avec permissions éditables (cases à cocher) si on peut gérer les rôles.</summary>
public sealed class RegimentRoleItemViewModel : ObservableObject
{
    private string _name;
    private bool _pMembers, _pRoles, _pInvite, _pRegiment, _pAlliances;

    public RegimentRoleItemViewModel(RegimentRoleDto dto, bool canEdit)
    {
        Id = dto.Id;
        _name = dto.Name;
        IsDefault = dto.IsDefault;
        CanEdit = canEdit;
        var perms = (RegimentPermission)dto.Permissions;
        _pMembers = perms.HasFlag(RegimentPermission.ManageMembers);
        _pRoles = perms.HasFlag(RegimentPermission.ManageRoles);
        _pInvite = perms.HasFlag(RegimentPermission.Invite);
        _pRegiment = perms.HasFlag(RegimentPermission.ManageRegiment);
        _pAlliances = perms.HasFlag(RegimentPermission.ManageAlliances);
    }

    public int Id { get; }
    public bool IsDefault { get; }
    public bool CanEdit { get; }
    public bool CanDelete => CanEdit && !IsDefault;

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool PMembers { get => _pMembers; set => Set(ref _pMembers, value); }
    public bool PRoles { get => _pRoles; set => Set(ref _pRoles, value); }
    public bool PInvite { get => _pInvite; set => Set(ref _pInvite, value); }
    public bool PRegiment { get => _pRegiment; set => Set(ref _pRegiment, value); }
    public bool PAlliances { get => _pAlliances; set => Set(ref _pAlliances, value); }

    public int ComposePermissions()
    {
        RegimentPermission p = RegimentPermission.None;
        if (PMembers) p |= RegimentPermission.ManageMembers;
        if (PRoles) p |= RegimentPermission.ManageRoles;
        if (PInvite) p |= RegimentPermission.Invite;
        if (PRegiment) p |= RegimentPermission.ManageRegiment;
        if (PAlliances) p |= RegimentPermission.ManageAlliances;
        return (int)p;
    }
}

/// <summary>Une alliance (acceptée ou demande), avec actions selon le sens.</summary>
public sealed class RegimentAllianceItemViewModel : ObservableObject
{
    public RegimentAllianceItemViewModel(RegimentAllianceDto dto)
    {
        RegimentId = dto.RegimentId;
        Name = dto.Name;
        Tag = dto.Tag;
        Faction = dto.Faction;
        Status = dto.Status;
        ProposedByUs = dto.ProposedByUs;
    }

    public string RegimentId { get; }
    public string Name { get; }
    public string Tag { get; }
    public string Faction { get; }
    public string Status { get; }
    public bool ProposedByUs { get; }

    public bool IsAccepted => Status == "accepted";
    public bool IsIncomingRequest => Status == "pending" && !ProposedByUs;
    public bool IsOutgoingRequest => Status == "pending" && ProposedByUs;

    public string Label => string.IsNullOrEmpty(Tag) ? Name : $"[{Tag}] {Name}";
    public string StateText => IsAccepted ? "Alliés" : IsOutgoingRequest ? "Demande envoyée" : "Demande reçue";
    public Brush FactionBrush => FactionColors.For(Faction);
}

/// <summary>Une invitation de régiment reçue.</summary>
public sealed class RegimentInviteItemViewModel : ObservableObject
{
    public RegimentInviteItemViewModel(RegimentInviteDto dto)
    {
        RegimentId = dto.RegimentId;
        Name = dto.Name;
        Tag = dto.Tag;
        Faction = dto.Faction;
        FromDisplayName = dto.FromDisplayName;
    }

    public string RegimentId { get; }
    public string Name { get; }
    public string Tag { get; }
    public string Faction { get; }
    public string FromDisplayName { get; }

    public string Label => string.IsNullOrEmpty(Tag) ? Name : $"[{Tag}] {Name}";
    public Brush FactionBrush => FactionColors.For(Faction);
}
