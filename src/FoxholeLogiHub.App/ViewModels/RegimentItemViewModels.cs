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
    private bool _pStockAdmin, _pStockCreate, _pStockShare, _pStockEdit, _pStockDelete, _pMpf;
    private bool _pResupRegiment, _pResupAlliance, _pResupManage;

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
        _pStockAdmin = perms.HasFlag(RegimentPermission.ManageStockpiles);
        _pStockCreate = perms.HasFlag(RegimentPermission.StockpileCreate);
        _pStockShare = perms.HasFlag(RegimentPermission.StockpileShare);
        _pStockEdit = perms.HasFlag(RegimentPermission.StockpileEdit);
        _pStockDelete = perms.HasFlag(RegimentPermission.StockpileDelete);
        _pMpf = perms.HasFlag(RegimentPermission.MpfManage);
        _pResupRegiment = perms.HasFlag(RegimentPermission.ResupplyRegiment);
        _pResupAlliance = perms.HasFlag(RegimentPermission.ResupplyAlliance);
        _pResupManage = perms.HasFlag(RegimentPermission.ResupplyManage);
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

    /// <summary>Parapluie logistique : couvre toutes les permissions granulaires ci-dessous.</summary>
    public bool PStockAdmin
    {
        get => _pStockAdmin;
        set
        {
            Set(ref _pStockAdmin, value);
            Raise(nameof(GranularEnabled));
        }
    }

    public bool PStockCreate { get => _pStockCreate; set => Set(ref _pStockCreate, value); }
    public bool PStockShare { get => _pStockShare; set => Set(ref _pStockShare, value); }
    public bool PStockEdit { get => _pStockEdit; set => Set(ref _pStockEdit, value); }
    public bool PStockDelete { get => _pStockDelete; set => Set(ref _pStockDelete, value); }
    public bool PMpf { get => _pMpf; set => Set(ref _pMpf, value); }
    public bool PResupRegiment { get => _pResupRegiment; set => Set(ref _pResupRegiment, value); }
    public bool PResupAlliance { get => _pResupAlliance; set => Set(ref _pResupAlliance, value); }
    public bool PResupManage { get => _pResupManage; set => Set(ref _pResupManage, value); }

    /// <summary>Les cases granulaires sont grisées quand le parapluie donne déjà tout.</summary>
    public bool GranularEnabled => CanEdit && !PStockAdmin;

    public int ComposePermissions()
    {
        RegimentPermission p = RegimentPermission.None;
        if (PMembers) p |= RegimentPermission.ManageMembers;
        if (PRoles) p |= RegimentPermission.ManageRoles;
        if (PInvite) p |= RegimentPermission.Invite;
        if (PRegiment) p |= RegimentPermission.ManageRegiment;
        if (PAlliances) p |= RegimentPermission.ManageAlliances;
        if (PStockAdmin) p |= RegimentPermission.ManageStockpiles;
        if (PStockCreate) p |= RegimentPermission.StockpileCreate;
        if (PStockShare) p |= RegimentPermission.StockpileShare;
        if (PStockEdit) p |= RegimentPermission.StockpileEdit;
        if (PStockDelete) p |= RegimentPermission.StockpileDelete;
        if (PMpf) p |= RegimentPermission.MpfManage;
        if (PResupRegiment) p |= RegimentPermission.ResupplyRegiment;
        if (PResupAlliance) p |= RegimentPermission.ResupplyAlliance;
        if (PResupManage) p |= RegimentPermission.ResupplyManage;
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
