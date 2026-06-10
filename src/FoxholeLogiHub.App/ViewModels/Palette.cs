using System.Windows.Media;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Brushes partagés et figés (Freeze) : une seule instance par couleur au lieu d'une allocation
/// à chaque lecture de binding — les ViewModels n'allouent plus de SolidColorBrush.
/// </summary>
public static class Palette
{
    public static readonly Brush Critical = Make(0xC0, 0x3A, 0x3A);     // rouge (critique / urgent)
    public static readonly Brush Warning = Make(0xC8, 0x8A, 0x2E);      // orange (bas / haute)
    public static readonly Brush Good = Make(0x3A, 0x8A, 0x4F);         // vert (bon / livrée)
    public static readonly Brush Neutral = Make(0x3A, 0x41, 0x4C);      // gris (aucun statut)
    public static readonly Brush GreenDark = Make(0x2F, 0x6B, 0x43);    // vert foncé (validé / partagé)
    public static readonly Brush BlueInfo = Make(0x2A, 0x6E, 0x8F);     // bleu (pris en charge)
    public static readonly Brush BrownOpen = Make(0x5A, 0x4A, 0x2A);    // brun (ouverte / privé)
    public static readonly Brush Slate = Make(0x33, 0x3A, 0x45);        // gris ardoise (boutons neutres)
    public static readonly Brush VisPublic = Make(0x4A, 0x5A, 0x2A);    // olive (visibilité publique)
    public static readonly Brush VisAlliance = Make(0x2A, 0x4A, 0x5A);  // bleu-gris (visibilité alliance)
    public static readonly Brush Online = Make(0x4C, 0xC2, 0x6A);       // vert vif (présence en ligne)
    public static readonly Brush Offline = Make(0x6B, 0x72, 0x80);      // gris (hors ligne)
    public static readonly Brush Wardens = Make(0x24, 0x5C, 0x8A);      // bleu Wardens
    public static readonly Brush Colonials = Make(0x51, 0x6C, 0x42);    // vert Colonials
    public static readonly Brush FactionNeutral = Make(0x44, 0x4A, 0x55);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
