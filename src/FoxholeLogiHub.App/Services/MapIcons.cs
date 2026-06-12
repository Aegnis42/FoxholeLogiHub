using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Icônes de la carte (PNG dans Data/mapicons). Trois familles :
/// - mod/{iconType}.png : icônes DÉJÀ COLORÉES du mod UI Label (récolte/production) ;
/// - rich/{iconType}.png : icônes détaillées blanc + détails noirs (source foxholestats.com),
///   teintées en MULTIPLIANT la couleur — le blanc prend la teinte, les détails restent sombres ;
/// - {iconType}.png : icônes officielles warapi, blanches plates, teintées en remplacement.
/// Les marqueurs utilisent les versions COMPOSÉES : teinte et contour sombre incrustés dans le
/// bitmap une seule fois (cache), au lieu d'un OpacityMask + DropShadowEffect par élément —
/// des milliers de surfaces intermédiaires en moins à chaque frame de zoom/pan.
/// </summary>
public static class MapIcons
{
    private static readonly string Dir =
        Path.Combine(AppContext.BaseDirectory, "Data", "mapicons");

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    /// <summary>Icône d'une structure/ressource par iconType War API (null si inconnue → repli emoji).</summary>
    public static ImageSource? ForStruct(int iconType) => Load(iconType.ToString());

    /// <summary>Icône pré-colorée du mod pour ce type, ou null → repli icône teintée.</summary>
    public static ImageSource? ForStructColored(int iconType) => Load(Path.Combine("mod", iconType.ToString()));

    /// <summary>Icône de base de ville par tier (1-3) ; tier hors plage → tier 1.</summary>
    public static ImageSource? ForTown(int tier) => Load($"town{Math.Clamp(tier, 1, 3)}");

    /// <summary>
    /// Marqueur de structure prêt à afficher : icône du mod telle quelle, sinon icône détaillée
    /// teintée par multiplication, sinon icône officielle teintée par remplacement — contour
    /// sombre incrusté dans tous les cas.
    /// </summary>
    public static ImageSource? ComposedStruct(int iconType, Color tint) =>
        Cache.GetOrAdd($"struct:{iconType}:{tint}", _ =>
        {
            if (Load(Path.Combine("mod", iconType.ToString())) is BitmapSource mod)
                return Compose(mod, null);
            if (Load(Path.Combine("rich", iconType.ToString())) is BitmapSource rich)
                return Compose(rich, tint, multiply: true);
            if (Load(iconType.ToString()) is BitmapSource official)
                return Compose(official, tint);
            return null;
        });

    /// <summary>Marqueur de ville prêt à afficher : icône officielle du tier, teinte faction incrustée.</summary>
    public static ImageSource? ComposedTown(int tier, Color tint) =>
        Cache.GetOrAdd($"town:{Math.Clamp(tier, 1, 3)}:{tint}", _ =>
            Load($"town{Math.Clamp(tier, 1, 3)}") is BitmapSource src ? Compose(src, tint) : null);

    private static ImageSource? Load(string name) => Cache.GetOrAdd(name, n =>
    {
        string path = Path.Combine(Dir, n + ".png");
        if (!File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 48; // affichées en 14-22 px écran
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    });

    /// <summary>
    /// Compose l'icône finale : contour sombre (alpha dilaté) puis l'icône par-dessus.
    /// Teinte : remplacement (silhouettes blanches plates) ou multiplication
    /// (<paramref name="multiply"/> — le blanc prend la couleur, les détails sombres restent).
    /// </summary>
    private static ImageSource? Compose(BitmapSource source, Color? tint, bool multiply = false)
    {
        try
        {
            var src = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);

            // 1. Alpha de l'icône (teinte appliquée aux sources blanches).
            var icon = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a == 0)
                    continue;
                if (tint is Color c)
                {
                    if (multiply)
                    {
                        icon[i + 0] = (byte)(pixels[i + 0] * c.B / 255);
                        icon[i + 1] = (byte)(pixels[i + 1] * c.G / 255);
                        icon[i + 2] = (byte)(pixels[i + 2] * c.R / 255);
                    }
                    else
                    {
                        icon[i + 0] = c.B;
                        icon[i + 1] = c.G;
                        icon[i + 2] = c.R;
                    }
                    icon[i + 3] = a;
                }
                else
                {
                    icon[i + 0] = pixels[i + 0];
                    icon[i + 1] = pixels[i + 1];
                    icon[i + 2] = pixels[i + 2];
                    icon[i + 3] = a;
                }
            }

            // 2. Contour : alpha dilaté (rayon 2 px sur 48 ≈ 0,7 px à l'écran), noir doux.
            var outline = new byte[pixels.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int max = 0;
                for (int dy = -2; dy <= 2 && max < 255; dy++)
                for (int dx = -2; dx <= 2 && max < 255; dx++)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                        continue;
                    int a = pixels[sy * stride + sx * 4 + 3];
                    if (a > max)
                        max = a;
                }
                if (max > 0)
                    outline[(y * stride) + x * 4 + 3] = (byte)(max * 0.85);
                // RGB = 0 (noir)
            }

            // 3. Icône PAR-DESSUS le contour (alpha over, non prémultiplié).
            var final = outline;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int ia = icon[i + 3];
                if (ia == 0)
                    continue;
                int oa = final[i + 3];
                int outA = ia + oa * (255 - ia) / 255;
                if (outA == 0)
                    continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int top = icon[i + ch] * ia;
                    int bottom = final[i + ch] * oa * (255 - ia) / 255;
                    final[i + ch] = (byte)((top + bottom) / outA);
                }
                final[i + 3] = (byte)outA;
            }

            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, final, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return source;
        }
    }
}
