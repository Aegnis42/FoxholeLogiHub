using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Un ami affiché dans la liste, avec statut de présence mis à jour en direct.</summary>
public sealed class FriendItemViewModel : ObservableObject
{
    private bool _online;

    public FriendItemViewModel(FriendDto dto, string? avatarUrl)
    {
        SteamId = dto.SteamId;
        DisplayName = dto.DisplayName;
        Faction = dto.Faction;
        AvatarUrl = dto.HasAvatar ? avatarUrl : null;
        _online = dto.Online;
    }

    public string SteamId { get; }
    public string DisplayName { get; }
    public string Faction { get; }

    /// <summary>URL de l'avatar (null si l'ami n'en a pas → on affiche l'initiale).</summary>
    public string? AvatarUrl { get; }

    public bool Online
    {
        get => _online;
        set
        {
            Set(ref _online, value);
            Raise(nameof(StatusText));
            Raise(nameof(StatusBrush));
        }
    }

    public string Initial =>
        string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    public string StatusText => Online ? "En ligne" : "Hors ligne";

    public Brush StatusBrush => Online
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6A))
        : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

    public Brush FactionBrush => FactionColors.For(Faction);
}

/// <summary>Une demande d'ami reçue (en attente d'acceptation).</summary>
public sealed class FriendRequestItemViewModel : ObservableObject
{
    public FriendRequestItemViewModel(FriendRequestDto dto, string? avatarUrl)
    {
        FromSteamId = dto.FromSteamId;
        DisplayName = dto.DisplayName;
        Faction = dto.Faction;
        AvatarUrl = dto.HasAvatar ? avatarUrl : null;
    }

    public string FromSteamId { get; }
    public string DisplayName { get; }
    public string Faction { get; }
    public string? AvatarUrl { get; }

    public string Initial =>
        string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    public Brush FactionBrush => FactionColors.For(Faction);
}

/// <summary>Couleurs de faction partagées.</summary>
internal static class FactionColors
{
    public static Brush For(string faction) => faction switch
    {
        "Wardens" => new SolidColorBrush(Color.FromRgb(0x24, 0x5C, 0x8A)),
        "Colonials" => new SolidColorBrush(Color.FromRgb(0x51, 0x6C, 0x42)),
        _ => new SolidColorBrush(Color.FromRgb(0x44, 0x4A, 0x55)),
    };
}
