using System.Windows.Media;

namespace FoxholeLogiHub.App.ViewModels;

/// <summary>
/// Brushes partagés et figés (Freeze) : une seule instance par couleur au lieu d'une allocation
/// à chaque lecture de binding — les ViewModels n'allouent plus de SolidColorBrush.
/// </summary>
public static class Palette
{
    public static readonly Brush Critical = Make(0xD0, 0x3A, 0x40);     // rouge (critique / urgent)
    public static readonly Brush Warning = Make(0xD9, 0x9A, 0x33);      // orange (bas / haute)
    public static readonly Brush Good = Make(0x2E, 0x9E, 0x57);         // vert (bon / livrée)
    public static readonly Brush Neutral = Make(0x3A, 0x45, 0x55);      // gris (aucun statut)
    public static readonly Brush GreenDark = Make(0x1F, 0x8A, 0x4C);    // vert foncé (validé / partagé)
    public static readonly Brush BlueInfo = Make(0x2D, 0x6F, 0xB8);     // bleu (pris en charge)
    public static readonly Brush BrownOpen = Make(0x6B, 0x55, 0x30);    // brun (ouverte / privé)
    public static readonly Brush Slate = Make(0x20, 0x2A, 0x38);        // gris ardoise (boutons neutres)
    public static readonly Brush VisPublic = Make(0x50, 0x65, 0x2E);    // olive (visibilité publique)
    public static readonly Brush VisAlliance = Make(0x27, 0x5A, 0x74);  // bleu-gris (visibilité alliance)
    public static readonly Brush Online = Make(0x41, 0xCC, 0x6E);       // vert vif (présence en ligne)
    public static readonly Brush Offline = Make(0x5E, 0x6B, 0x7E);      // gris (hors ligne)
    public static readonly Brush Wardens = Make(0x24, 0x5C, 0x8A);      // bleu Wardens
    public static readonly Brush Colonials = Make(0x51, 0x6C, 0x42);    // vert Colonials
    public static readonly Brush FactionNeutral = Make(0x46, 0x50, 0x5E);

    // Remplissages de la carte (semi-transparents pour laisser respirer le fond).
    public static readonly Brush MapWarden = MakeA(0x6E, 0x24, 0x5C, 0x8A);
    public static readonly Brush MapColonial = MakeA(0x6E, 0x51, 0x6C, 0x42);
    public static readonly Brush MapContested = MakeA(0x58, 0xC8, 0x8A, 0x2E);
    public static readonly Brush MapNeutral = MakeA(0x2E, 0x6B, 0x72, 0x80);
    public static readonly Brush MapStroke = Make(0x10, 0x13, 0x18);

    // Teintes des icônes officielles de la carte (icônes blanches → teintées par faction).
    // Plus claires que les couleurs de zone pour rester lisibles sur le terrain.
    public static readonly Brush IconWarden = Make(0x6F, 0xB1, 0xE8);
    public static readonly Brush IconColonial = Make(0xA8, 0xC8, 0x7A);
    public static readonly Brush IconNeutral = Make(0xC9, 0xD1, 0xDC);
    public static readonly Brush MapResourceTint = Make(0xE3, 0xD9, 0xC2);
    public static readonly Brush MapTownNeutral = Make(0x9A, 0xA4, 0xB2);

    // Sous-régions (zones d'influence des villes) — plus saturées, façon FoxholeStats.
    public static readonly Brush CellWarden = MakeA(0x8C, 0x24, 0x5C, 0x8A);
    public static readonly Brush CellColonial = MakeA(0x8C, 0x51, 0x6C, 0x42);
    public static readonly Brush CellNeutral = MakeA(0x46, 0x6B, 0x72, 0x80);
    public static readonly Brush CellScorched = MakeA(0x86, 0x6E, 0x22, 0x22);
    public static readonly Brush CellStroke = MakeA(0x50, 0x0E, 0x11, 0x16);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush MakeA(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
