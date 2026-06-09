using System.Windows.Media;
using FoxholeLogiHub.Contracts;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>Un ami affiché dans la liste, avec statut de présence mis à jour en direct.</summary>
public sealed class FriendItemViewModel : ObservableObject
{
    private bool _online;

    public FriendItemViewModel(FriendDto dto)
    {
        SteamId = dto.SteamId;
        DisplayName = dto.DisplayName;
        Faction = dto.Faction;
        _online = dto.Online;
    }

    public string SteamId { get; }
    public string DisplayName { get; }
    public string Faction { get; }

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
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6A))   // vert
        : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));  // gris

    public Brush FactionBrush => Faction switch
    {
        "Wardens" => new SolidColorBrush(Color.FromRgb(0x24, 0x5C, 0x8A)),
        "Colonials" => new SolidColorBrush(Color.FromRgb(0x51, 0x6C, 0x42)),
        _ => new SolidColorBrush(Color.FromRgb(0x44, 0x4A, 0x55)),
    };
}
